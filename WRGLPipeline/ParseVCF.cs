using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.IO;

namespace WRGLPipeline
{
    public struct Annotation
    {
        public string Effect, Effect_Impact, Functional_Class, Codon_Change, Amino_Acid_Change, Amino_Acid_length, Gene_Name, Transcript_BioType, Gene_Coding, Transcript_ID, Exon_Rank, Genotype_Number;
    }

    public struct GenomicVariant
    {
        public string CHROM, REF, ALT;
        public UInt32 POS;
    }

    public struct VCFRecordWithGenotype
    {
        public string CHROM, ID, REF, ALT, FILTER;
        public UInt32 POS;
        public double QUAL;
        public Dictionary<string, string> infoSubFields;
        public Dictionary<string, string> formatSubFields;
    }

    class ParseVCF //process a single VCF (single or multisample) and returns a dictionary (SampleID:List<VCFRecord>) of records
    {
        private string VCFPath;
        private string logFilename;
        private List<string> VCFMetaLines = new List<string>();
        private List<string> VCFBody = new List<string>();
        private List<string> VCFHeader = new List<string>();
        private HashSet<string> infoRecordHeaders = new HashSet<string>();
        private HashSet<string> formatRecordHeaders = new HashSet<string>();
        private bool hasGenotypes = false;
        private ProgrammeParameters parameters;
        private Dictionary<string, List<VCFRecordWithGenotype>> VCFRecords = new Dictionary<string, List<VCFRecordWithGenotype>>();
        private Dictionary<GenomicVariant, HashSet<Annotation>> annotations = new Dictionary<GenomicVariant, HashSet<Annotation>>();

        public ParseVCF(string _VCFPath, string _logFilename, ProgrammeParameters _parameters)
        {
            this.VCFPath = _VCFPath; //VCF filename
            this.logFilename = _logFilename; //log filename
            this.parameters = _parameters;

            AuxillaryFunctions.WriteLog(@"Parsing " + VCFPath, logFilename, 0, false, parameters);

            getVCFRecordsWithGenotypes();
            ParseSnpEff4E();
        }

        private void getVCFRecordsWithGenotypes()
        {   
            VCFRecordWithGenotype VCFRecordTemp;

            SplitVCFHeaderandBody(); //read VCF into memory
            getColumnHeaders();
            checkColumnHeaders(); 
            getInfoandFormatSubHeaders(); //from metalines

            if (hasGenotypes == true){
                
                //iterate over Sample_IDs (horizontally)
                for (int k = 9; k < VCFHeader.Count; ++k)
                {
                    VCFRecords.Add(VCFHeader[k], new List<VCFRecordWithGenotype>()); //prepare dictionary
                }

            }
            else
            {
                VCFRecords.Add("", new List<VCFRecordWithGenotype>()); //prepare dictionary
            }

            //iterate down the VCF body
            foreach (string line in VCFBody)
            {
                string[] bodyFields = line.Split('\t');

                VCFRecordTemp.CHROM = bodyFields[0]; //required, string
                VCFRecordTemp.POS = Convert.ToUInt32(bodyFields[1]); //required, integer
                VCFRecordTemp.ID = bodyFields[2]; //not required, string or '.'
                VCFRecordTemp.REF = bodyFields[3]; //required, string
                VCFRecordTemp.ALT = bodyFields[4]; //not required, string or '.'

                if (bodyFields[5] == ".") //missing value
                {
                    VCFRecordTemp.QUAL = 0; //surrogate value; no probability that this variant is not an error
                }
                else
                {
                    VCFRecordTemp.QUAL = Convert.ToDouble(bodyFields[5]);
                }

                VCFRecordTemp.FILTER = bodyFields[6];

                if (bodyFields[7] != ".") //missing values
                {
                    VCFRecordTemp.infoSubFields = ExtractInfoBody(bodyFields[7]);
                }
                else
                {
                    VCFRecordTemp.infoSubFields = new Dictionary<string, string>(); //empty dictionary
                }

                if (hasGenotypes == true)
                {
                    //iterate over Sample_IDs (horizontally)
                    for (int k = 9; k < VCFHeader.Count; ++k) //skip common headers
                    {
                        if (bodyFields[k] != ".") //missing values
                        {
                            VCFRecordTemp.formatSubFields = ExtractFormatBody(bodyFields[8], bodyFields[k]);
                        }
                        else
                        {
                            VCFRecordTemp.formatSubFields = new Dictionary<string, string>(); //empty dictionary
                        }

                        //bank struct by Sample_ID
                        VCFRecords[VCFHeader[k]].Add(VCFRecordTemp);
                    }
                }
                else
                {
                    VCFRecordTemp.formatSubFields = new Dictionary<string, string>(); //empty dictionary
                    VCFRecords[""].Add(VCFRecordTemp);
                }

            } //done looping over VCF body

        }

        private void SplitVCFHeaderandBody() //extract VCF headers and body
        {
            bool FirstLine = true;
            string VCFLine;

            // Read the file and display it line by line.
            StreamReader file = new StreamReader(VCFPath);

            while ((VCFLine = file.ReadLine()) != null)
            {
                if (VCFLine == "")
                {
                    continue;
                }
                else if (FirstLine == true)
                {

                    if (VCFLine != "##fileformat=VCFv4.1" && VCFLine != "##fileformat=VCFv4.2")
                    {
                        AuxillaryFunctions.WriteLog(@"File format not VCF v4.1 or v4.2, Parser may not function correctly", logFilename, 1, false, parameters);
                    }

                    FirstLine = false;

                }
                else if (VCFLine[0] == '#')
                {
                    VCFMetaLines.Add(VCFLine);
                }
                else
                {
                    VCFBody.Add(VCFLine);
                }
            }
        }

        private void getColumnHeaders() //true = has genotypes false = no genotypes
        {

            foreach (string header in VCFMetaLines[VCFMetaLines.Count - 1].Split('\t'))
            {
                VCFHeader.Add(header);
            }

            if (VCFHeader.Count < 8)
            {
                AuxillaryFunctions.WriteLog(@"Malformed VCF. Too few column headers.", logFilename, -1, false, parameters);
                throw new FormatException();
            }
            else if (VCFHeader.Count == 8 || VCFHeader.Count == 9)// stop reading VCF
            {
                AuxillaryFunctions.WriteLog(@"VCF has no genotypes", logFilename, 1, false, parameters);
            }
            else
            {
                hasGenotypes = true;
            }
        }

        private void checkColumnHeaders()
        {
            int n = 0;

            foreach (string header in VCFHeader)
            {
                if (n == 0)
                {
                    if (header != "#CHROM")
                    {
                        AuxillaryFunctions.WriteLog(@"Malformed VCF. Incorrect column header format.", logFilename, -1, false, parameters);
                        throw new FormatException();
                    }
                }
                else if (n == 1)
                {
                    if (header != "POS")
                    {
                        AuxillaryFunctions.WriteLog(@"Malformed VCF. Incorrect column header format.", logFilename, -1, false, parameters);
                        throw new FormatException();
                    }
                }
                else if (n == 2)
                {
                    if (header != "ID")
                    {
                        AuxillaryFunctions.WriteLog(@"Malformed VCF. Incorrect column header format.", logFilename, -1, false, parameters);
                        throw new FormatException();
                    }
                }
                else if (n == 3)
                {
                    if (header != "REF")
                    {
                        AuxillaryFunctions.WriteLog(@"Malformed VCF. Incorrect column header format.", logFilename, -1, false, parameters);
                        throw new FormatException();
                    }
                }
                else if (n == 4)
                {
                    if (header != "ALT")
                    {
                        AuxillaryFunctions.WriteLog(@"Malformed VCF. Incorrect column header format.", logFilename, -1, false, parameters);
                        throw new FormatException();
                    }
                }
                else if (n == 5)
                {
                    if (header != "QUAL")
                    {
                        AuxillaryFunctions.WriteLog(@"Malformed VCF. Incorrect column header format.", logFilename, -1, false, parameters);
                        throw new FormatException();
                    }
                }
                else if (n == 6)
                {
                    if (header != "FILTER")
                    {
                        AuxillaryFunctions.WriteLog(@"Malformed VCF. Incorrect column header format.", logFilename, -1, false, parameters);
                        throw new FormatException();
                    }
                }
                else if (n == 7)
                {
                    if (header != "INFO")
                    {
                        AuxillaryFunctions.WriteLog(@"Malformed VCF. Incorrect column header format.", logFilename, -1, false, parameters);
                        throw new FormatException();
                    }
                }
                else if (n == 8)
                {
                    if (header != "FORMAT")
                    {
                        AuxillaryFunctions.WriteLog(@"Malformed VCF. Incorrect column header format.", logFilename, -1, false, parameters);
                        throw new FormatException();
                    }
                    
                    break; //no point checking after column 8 (these are sample specific)
                }

                ++n;
            }
        }

        private void getInfoandFormatSubHeaders()
        {
            string[] fields;
            string infoRecordHeader = @"^##INFO";
            string formatRecordHeader = @"^##FORMAT";
            Regex infoRecordHeaderRgx = new Regex(infoRecordHeader);
            Regex formatRecordHeaderRgx = new Regex(formatRecordHeader);

            foreach (string metaLine in VCFMetaLines)
            {
                if (infoRecordHeaderRgx.IsMatch(metaLine))
                {
                    fields = metaLine.Split('=', ',');
                    infoRecordHeaders.Add(fields[2]);
                }
                else if (formatRecordHeaderRgx.IsMatch(metaLine))
                {
                    fields = metaLine.Split('=', ',');
                    formatRecordHeaders.Add(fields[2]);
                }
            }
        }

        private Dictionary<string, string> ExtractInfoBody(string infoField) //operate line-by-line
        {
            Dictionary<string, string> infoSubFields = new Dictionary<string, string>();
            string last = "", beforelast = "";
            string pattern = @"(=|;)";
            string[] tokens = Regex.Split(infoField, pattern);

            foreach (string token in tokens)
            {
                if (last == "=")
                {
                    infoSubFields.Add(beforelast, token);
                    infoRecordHeaders.Add(beforelast); //ensure all info headers are in the headers set
                }

                beforelast = last;
                last = token;
            }

            return infoSubFields;
        }

        private Dictionary<string, string> ExtractFormatBody(string formatField, string formatValues) //operate line-by-line
        {
            Dictionary<string, string> formatSubFields = new Dictionary<string, string>();
            string[] fields = formatField.Split(':');
            string[] values = formatValues.Split(':');

            if (fields.Length != values.Length) //missing values
            {
                AuxillaryFunctions.WriteLog(@"Some genotype fields are missing values, blank values reported", logFilename, 1, false, parameters);

                //add genotype column
                formatSubFields.Add(fields[0], values[0]); //first column is always GT

                //ignore other columns
                for (int n = 1; n < fields.Length; ++n)
                {
                    formatSubFields.Add(fields[n], "");
                }
            }
            else
            {
                for (int n = 0; n < fields.Length; ++n)
                {
                    formatSubFields.Add(fields[n], values[n]);
                }
            }

            //ensure all format headers are in the headers set
            foreach (string fieldHeader in fields)
            {
                formatRecordHeaders.Add(fieldHeader);
            }

            return formatSubFields;
        }

        private void ParseSnpEff4E()
        {
            string sequence_featureRgxString = @"^sequence_feature"; //skip these annotations
            Regex sequence_featureRgx = new Regex(sequence_featureRgxString);
            Annotation tempAnnotation;
            GenomicVariant tempGenomicVariant;
            string effField;

            //iterate over SampleIDs
            foreach (KeyValuePair<string, List<VCFRecordWithGenotype>> iter in VCFRecords)
            {
                //iterate over variants
                foreach (VCFRecordWithGenotype record in iter.Value)
                {
                    if (record.infoSubFields.ContainsKey(@"EFF") == true) //annotation available for this variant
                    {
                        //save eff field for lookups
                        effField = record.infoSubFields[@"EFF"];

                        //make genomic variant key
                        tempGenomicVariant.CHROM = record.CHROM;
                        tempGenomicVariant.POS = record.POS;
                        tempGenomicVariant.REF = record.REF;
                        tempGenomicVariant.ALT = record.ALT;

                        //get eff subfields
                        string[] effSubFields = effField.Split(',');

                        foreach (string effSubField in effSubFields)
                        {
                            string[] effAnnotations = effSubField.Split('(', ')', '|');

                            //skip sequence_feature fields
                            if (sequence_featureRgx.IsMatch(effAnnotations[0]))
                            {
                                continue;
                            }

                            tempAnnotation.Effect = effAnnotations[0];
                            tempAnnotation.Effect_Impact = effAnnotations[1];
                            tempAnnotation.Functional_Class = effAnnotations[2];
                            tempAnnotation.Codon_Change = effAnnotations[3];
                            tempAnnotation.Amino_Acid_Change = effAnnotations[4];
                            tempAnnotation.Amino_Acid_length = effAnnotations[5];
                            tempAnnotation.Gene_Name = effAnnotations[6];
                            tempAnnotation.Transcript_BioType = effAnnotations[7];
                            tempAnnotation.Gene_Coding = effAnnotations[8];
                            tempAnnotation.Transcript_ID = effAnnotations[9];
                            tempAnnotation.Exon_Rank = effAnnotations[10];
                            tempAnnotation.Genotype_Number = effAnnotations[11];

                            if (annotations.ContainsKey(tempGenomicVariant) == true)
                            {
                                annotations[tempGenomicVariant].Add(tempAnnotation);
                            }
                            else
                            {
                                annotations.Add(tempGenomicVariant, new HashSet<Annotation>());
                                annotations[tempGenomicVariant].Add(tempAnnotation);
                            }

                        }
                    }
                }
            }
        }

        //interograte if the VCF has genotypes
        public bool isVCFContainGenotypes { get { return hasGenotypes; } }
        public Dictionary<string, List<VCFRecordWithGenotype>> getVCFRecords { get { return VCFRecords; } }
        public Dictionary<GenomicVariant, HashSet<Annotation>> getSnpEffAnnotations { get { return annotations; } }
    }
}
