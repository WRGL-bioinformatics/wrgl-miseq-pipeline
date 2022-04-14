using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;

namespace WRGLPipeline
{
    // DEV: Might be worth either marking this as a specific SNPEff annotation, or seeing
    //      if we can do some kind of inheritence type tricker to standardise the fields to
    //      allow us to use SNPEff but also have the option to expand to different tools.
    // NOTE: Perhaps more importantly, this would let us change from the older EFF-style
    //       annotations to ANN-style, which is in the VCF standard.
    struct Annotation
    {
        public string Effect { get; set; }
        public string Effect_Impact { get; set; }
        public string Functional_Class { get; set; }
        public string Codon_Change { get; set; }
        public string Amino_Acid_Change { get; set; }
        public string Amino_Acid_length { get; set; }
        public string Gene_Name { get; set; }
        public string Transcript_BioType { get; set; }
        public string Gene_Coding { get; set; }
        public string Transcript_ID { get; set; }
        public string Exon_Rank { get; set; }
        public string Genotype_Number { get; set; }
        
        /// <summary>
        /// Variant annotation details from SNPEff.
        /// Additional details at https://pcingola.github.io/SnpEff/se_inputoutput/#eff-field-vcf-output-files
        /// NOTE: No validation is performed on these inputs.
        /// </summary>
        /// <param name="effect">Functional effect (e.g. coding/non-coding)</param>
        /// <param name="impact">Estimated functional impact</param>
        /// <param name="functional_class">Functional class (no further details avaiable)</param>
        /// <param name="codon_change">Full 3bp codon change</param>
        /// <param name="aa_change">Amino acid change</param>
        /// <param name="aa_length">Full length of protein</param>
        /// <param name="gene">Gene name/symbol</param>
        /// <param name="biotype">Biotype of transcript (e.g. coding, non-coding RNA)</param>
        /// <param name="gene_coding">is gene coding or non-coding</param>
        /// <param name="transcript">Transcript ID (usually ENST)</param>
        /// <param name="exon">Exon number</param>
        /// <param name="genotype_number">Number of ALT genotype at this variant position.</param>
        public Annotation(string effect, string impact, string functional_class, string codon_change,
                          string aa_change, string aa_length, string gene, string biotype, string gene_coding,
                          string transcript, string exon, string genotype_number)
        {
            this.Effect = effect;
            this.Effect_Impact = impact;
            this.Functional_Class = functional_class;
            this.Codon_Change = codon_change;
            this.Amino_Acid_Change = aa_change;
            this.Amino_Acid_length = aa_length;
            this.Gene_Name = gene;
            this.Transcript_BioType = biotype;
            this.Gene_Coding = gene_coding;
            this.Transcript_ID = transcript;
            this.Exon_Rank = exon;
            this.Genotype_Number = genotype_number;
        }
        public override String ToString()
        {
            return $"<Annotation: {this.Transcript_ID}:{this.Amino_Acid_Change}>";
        }
    }

    struct GenomicVariant
    {
        public string CHROM { get; private set; }
        public int POS { get; private set; }
        public string REF { get; private set; }
        public string ALT { get; private set; }

        /// <summary>
        /// A single variant by genomic position
        /// </summary>
        /// <param name="CHROM">Chromosome</param>
        /// <param name="POS">Chromsome position</param>
        /// <param name="REF">Reference allele</param>
        /// <param name="ALT">Alternative allele</param>
        /// <remarks>This is build independent - it will work for hg19 or GRCh38</remarks>
        public GenomicVariant(string CHROM, int POS, string REF, string ALT)
        {
            this.CHROM = CHROM;
            this.POS = POS;
            this.REF = REF;
            this.ALT = ALT;            
        }
        public override String ToString()
        {
            return $"<GenomicVariant: {this.CHROM}:{this.POS}{this.REF}>{this.ALT}>";
        }
    }

    struct VCFRecordWithGenotype
    {
        public string CHROM { get; private set; }
        public int POS { get; set; }
        public string ID { get; set; }
        public string REF { get; set; }
        public string ALT { get; set; }
        public string FILTER { get; set; }
        public double QUAL { get; set; }
        public Dictionary<string, string> INFO { get; set; }
        public Dictionary<string, string> FORMAT { get; set; }

        /// <summary>
        /// Full VCF details for a variant position
        /// </summary>
        /// <param name="CHROM">Chromosome</param>
        /// <param name="POS">Chromosome Position</param>
        /// <param name="ID">ID if present (e.g. dbSNP rs number)</param>
        /// <param name="REF">Reference Allele</param>
        /// <param name="ALT">Alternative Allele</param>
        /// <param name="QUAL">Genotype quality (overall, from VCF)</param>
        /// <param name="FILTER">VCF Filters (PASS, etc)</param>
        /// <param name="INFO"></param>
        /// <param name="FORMAT"></param>
        /// <remarks>DEV: QUAL is from the overall VCF, which is for the merged multi sample file. It might be worth extracting the per-sample genotype quality at some point (if possible)</remarks>
        public VCFRecordWithGenotype(string CHROM, int POS, string ID, string REF, string ALT, double QUAL, string FILTER,Dictionary<string, string> INFO, Dictionary<string, string> FORMAT)
        {
            this.CHROM = CHROM;
            this.POS = POS;
            this.ID = ID;
            this.REF = REF;
            this.ALT = ALT;
            this.QUAL = QUAL;
            this.FILTER = FILTER;
            this.INFO = INFO;
            this.FORMAT = FORMAT;
        }
        public override String ToString()
        {
            return $"<VCFRecordWithGenotype: {this.CHROM}:{this.POS}{this.REF}>{this.ALT}>";
        }
    }

    class ParseVCF
    {
        private readonly string VCFPath;
        private readonly List<string> VCFMetaLines = new List<string>();
        private readonly List<string> VCFBody = new List<string>();
        private readonly List<string> VCFHeader = new List<string>();
        // DEV: What are these even used for?
        //private readonly HashSet<string> infoRecordHeaders = new HashSet<string>();
        //private readonly HashSet<string> formatRecordHeaders = new HashSet<string>();
        private readonly ProgrammeParameters parameters;
        public Dictionary<string, List<VCFRecordWithGenotype>> VCFRecords { get; set; } = new Dictionary<string, List<VCFRecordWithGenotype>>();
        // Non-readonly variables
        private bool hasGenotypes = false;
        // Public properties
        public Dictionary<GenomicVariant, HashSet<Annotation>> SnpEffAnnotations { get; private set; } = new Dictionary<GenomicVariant, HashSet<Annotation>>();

        /// <summary>
        /// Process a single VCF (single or multisample) and returns a dictionary (SampleID:List&lt;VCFRecord&gt;) of records
        /// </summary>
        /// <param name="VCFPath">Path to the VCF file</param>
        /// <param name="parameters">Configured ProgrammeParameters</param>
        public ParseVCF(string VCFPath, ProgrammeParameters parameters)
        {
            this.VCFPath = VCFPath;
            this.parameters = parameters;

            AuxillaryFunctions.WriteLog(@"Parsing " + VCFPath, parameters);

            // Moved from GetVCFRecordsWithGenotypes to constructor.
            // DEV: These seem like they could be quite difficult to write tests for without a bit of a
            //      change in the way they work
            SplitVCFHeaderandBody();
            GetColumnHeaders();
            CheckColumnHeaders();
            GetInfoandFormatSubHeaders();

            GetVCFRecordsWithGenotypes();
            ParseSnpEff4EFF();
        }

        /// <summary>
        /// Extracts VCRecordWithGenotypes for all variants which have sample genotypes
        /// </summary>
        /// <remarks>VCFRecordWithGenotype is probaly a bit redundant, as it doesn't 
        /// seem to store any useful information for variants without - as the check for
        /// this suggests that these varaints have no *sample* information.
        /// </remarks>
        private void GetVCFRecordsWithGenotypes()
        {
            // Populate the header column
            // DEV: Rrefactor?
            // DEV: I don't like that hasGenotypes is set from within a function with a different purpose
            //      it should probably have a dedicated checker, which should assign the hasGenotypes bool
            //      explicitly from a return value.
            if (hasGenotypes == true){                
                //iterate over Sample_IDs (horizontally)
                for (int k = 9; k < VCFHeader.Count; ++k)
                {
                    VCFRecords.Add(VCFHeader[k], new List<VCFRecordWithGenotype>()); //prepare dictionary
                }
            }
            else
            {
                // This is needed for reading BCInterpretations, which doesn't have any genotypes.
                VCFRecords.Add("", new List<VCFRecordWithGenotype>());
            }

            // Run through each variant in the body of the VCF file, skipping any comment lines
            // DEV: I wonder if it might be more memory efficient to do this from a StreamReader...
            foreach (string line in VCFBody)
            {
                string[] fields = line.Split('\t');

                // Create the base VCFRecord assuming no sample genotypes
                // Doing this here saves having to create a new one for each sample
                VCFRecordWithGenotype VCFRecordTemp = new VCFRecordWithGenotype(CHROM: fields[0], POS: int.Parse(fields[1]), ID: fields[2], REF: fields[3],
                                                                                    ALT: fields[4], QUAL: fields[5] == "." ? 0 : Convert.ToDouble(fields[5]),
                                                                                    FILTER: fields[6], INFO: fields[7] == "." ? new Dictionary<string, string>() : ExtractInfoBody(fields[7]),
                                                                                    FORMAT: new Dictionary<string, string>());

                if (hasGenotypes == true)
                {
                    // Iterate over sample IDs by the header column, create a new VCFRecordWithGenotype from each samples
                    // FORMAT data (and the overall variant data)
                    for (int k = 9; k < VCFHeader.Count; ++k) //skip common headers
                    {
                        // Update the temp record, rather than instantiating a new one for each sample
                        VCFRecordTemp.FORMAT = fields[k] == "." ? new Dictionary<string, string>() : ExtractFormatBody(fields[8], fields[k]);
                        //bank struct by Sample_ID
                        VCFRecords[VCFHeader[k]].Add(VCFRecordTemp);
                    }
                }
                else
                {
                    //bank struct by Sample_ID
                    VCFRecords[""].Add(VCFRecordTemp);
                }
            }
        }

        /// <summary>
        /// Reads the VCF File into memory and splits out the header and the body.
        /// </summary>
        /// <remarks>DEV: It might be more efficient to deal with two separate StreamReader functions - read header
        /// and read body. Currently this reads the whole file into memory, which is inefficient and makes no real
        /// sense since we still process it one variant at a time.
        /// With that in mind, there's not need to spend too much time trying to make this too efficient...</remarks>
        private void SplitVCFHeaderandBody() //extract VCF headers and body
        {
            bool FirstLine = true;
            string VCFLine;

            // Read the file and display it line by line.
            using (FileStream stream = new FileStream(VCFPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader file = new StreamReader(stream))
            {
                while ((VCFLine = file.ReadLine()) != null)
                {
                    if (VCFLine == "")
                    {
                        continue;
                    }
                    else if (FirstLine == true)
                    {
                        // DEV: TODO: There is now a version 4.3
                        //            We should probably extract the version number double, and check that
                        //            it is not *below* 4.1? OR add in a latest tested version and check it is
                        //            between those values, just in case future VCF specs have breaking changes.
                        if (VCFLine != "##fileformat=VCFv4.1" && VCFLine != "##fileformat=VCFv4.2")
                        {
                            AuxillaryFunctions.WriteLog(@"File format not VCF v4.1 or v4.2, Parser may not function correctly", parameters, errorCode: 1);
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
        }

        /// <summary>
        /// Extracts the VCF column headers (CHROM, POS, etc.) from the split header
        /// </summary>
        /// <remarks>Could return the header list, rather than editing it in place - more testable?</remarks>
        private void GetColumnHeaders()
        {
            // This takes the last line of the VCFMetaLines, which is all the header lines (e.g.
            // those starting with "#". This last line is the column headers. Alternatively, we could
            // split by finding the row starting with a single "#" or "#CHROM", as all other header lines
            // start "##".
            foreach (string header in VCFMetaLines[VCFMetaLines.Count - 1].Split('\t'))
            {
                VCFHeader.Add(header);
            }

            // DEV: This should really be in CheckColumnHeaders, but that should be called here so we
            //      have check the headers before trying to tell if there are genotypes or not.
            //      But first it needs to be re-written to return a bool.
            if (VCFHeader.Count < 8)
            {
                AuxillaryFunctions.WriteLog(@"Malformed VCF. Too few column headers.", parameters, errorCode: -1);
                throw new FormatException();
            }
            // Fields 8 & 9 are the INFO and FORMAT columns respectively. FORMAT may or may not be present,
            // which is why it's checking both possibilties. Fields 10 and beyond are sample IDs, so length 8 and
            // 9 are the only options for valid VCF without any sample information.
            // This should be in CheckColumnHeaders(), as then it logically returns a boolean rather than any other
            // processed data
            else if (VCFHeader.Count == 8 || VCFHeader.Count == 9)
            {
                AuxillaryFunctions.WriteLog(@"VCF has no genotypes", parameters, errorCode: 1);
            }
            else
            {
                hasGenotypes = true;
            }
        }

        /// <summary>
        /// Checks the VCF column headers for validity. Does nothing if all is ok, otherwise throws expections on specific issues.
        /// </summary>
        /// <remarks>Why isn't this called when the headers are split?</remarks>
        private void CheckColumnHeaders()
        {
            // Check the overall number of columns
            if (VCFHeader.Count < 8)
            {
                AuxillaryFunctions.WriteLog(@"Malformed VCF. Too few column headers.", parameters, errorCode: -1);
                throw new FormatException();
            }

            // DEV: Is there some kind of enumerate function we could use here to loop through all the headers
            //      without having to manually use the counter variable n?
            int n = 0;
            foreach (string header in VCFHeader)
            {
                if (n == 0)
                {
                    if (header != "#CHROM")
                    {
                        AuxillaryFunctions.WriteLog(@"Malformed VCF. Incorrect column header format.", parameters, errorCode: -1);
                        throw new FormatException();
                    }
                }
                else if (n == 1)
                {
                    if (header != "POS")
                    {
                        AuxillaryFunctions.WriteLog(@"Malformed VCF. Incorrect column header format.", parameters, errorCode: -1);
                        throw new FormatException();
                    }
                }
                else if (n == 2)
                {
                    if (header != "ID")
                    {
                        AuxillaryFunctions.WriteLog(@"Malformed VCF. Incorrect column header format.",parameters, errorCode: -1);
                        throw new FormatException();
                    }
                }
                else if (n == 3)
                {
                    if (header != "REF")
                    {
                        AuxillaryFunctions.WriteLog(@"Malformed VCF. Incorrect column header format.", parameters, errorCode: -1);
                        throw new FormatException();
                    }
                }
                else if (n == 4)
                {
                    if (header != "ALT")
                    {
                        AuxillaryFunctions.WriteLog(@"Malformed VCF. Incorrect column header format.", parameters, errorCode: -1);
                        throw new FormatException();
                    }
                }
                else if (n == 5)
                {
                    if (header != "QUAL")
                    {
                        AuxillaryFunctions.WriteLog(@"Malformed VCF. Incorrect column header format.", parameters, errorCode: -1);
                        throw new FormatException();
                    }
                }
                else if (n == 6)
                {
                    if (header != "FILTER")
                    {
                        AuxillaryFunctions.WriteLog(@"Malformed VCF. Incorrect column header format.", parameters, errorCode: -1);
                        throw new FormatException();
                    }
                }
                else if (n == 7)
                {
                    if (header != "INFO")
                    {
                        AuxillaryFunctions.WriteLog(@"Malformed VCF. Incorrect column header format.", parameters, errorCode: -1);
                        throw new FormatException();
                    }
                }
                else if (n == 8)
                {
                    if (header != "FORMAT")
                    {
                        AuxillaryFunctions.WriteLog(@"Malformed VCF. Incorrect column header format.", parameters, errorCode: -1);
                        throw new FormatException();
                    }
                    // There is no point checking after column 8 (these are sample specific)
                    break; 
                }
                ++n;
            }
        }

        /// <summary>
        /// Extracts the INFO and FORMAT definitions from the header
        /// e.g. The lines detailing these fields, not the variant data itself
        /// </summary>
        /// <remarks>Where is this actually used? To write a new VCF?
        /// I don't think this is ever actually used!
        /// </remarks>
        private void GetInfoandFormatSubHeaders()
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
                    //infoRecordHeaders.Add(fields[2]);
                }
                else if (formatRecordHeaderRgx.IsMatch(metaLine))
                {
                    fields = metaLine.Split('=', ',');
                    //formatRecordHeaders.Add(fields[2]);
                }
            }
        }

        /// <summary>
        /// Splits out the INFO field of a variant into the key:value sub fields
        /// e.g. DP=100 -> infoField["DP"] = 100 
        /// </summary>
        /// <param name="infoField">Variant INFO field to process</param>
        /// <returns>Dictionary of the INFO sub fields and their corresponding values</returns>
        private Dictionary<string, string> ExtractInfoBody(string infoField)
        {
            Dictionary<string, string> infoSubFields = new Dictionary<string, string>();
            string last = "";
            string beforelast = "";
            string pattern = @"(=|;)";
            string[] tokens = Regex.Split(infoField, pattern);

            foreach (string token in tokens)
            {
                if (last == "=")
                {
                    infoSubFields.Add(beforelast, token);
                    //infoRecordHeaders.Add(beforelast); //ensure all info headers are in the headers set
                }
                beforelast = last;
                last = token;
            }
            return infoSubFields;
        }

        /// <summary>
        /// Extracts the FORMAT subfields from the per-sample information
        /// </summary>
        /// <param name="formatHeaders">Expected headers from the FORMAT definition field</param>
        /// <param name="sampleFormatField">Per-sample variant details</param>
        /// <returns>Dictionary of per-sample FORMAT subfields and their corresponding values</returns>
        private Dictionary<string, string> ExtractFormatBody(string formatHeaders, string sampleFormatField) //operate line-by-line
        {
            Dictionary<string, string> formatSubFields = new Dictionary<string, string>();
            string[] fields = formatHeaders.Split(':');
            string[] values = sampleFormatField.Split(':');

            // DEV: Missing values should be handled during the loop??
            //      formatHeaders is derived from the variant FORMAT field, and although this can vary
            //      between variants I've never seen it change between samples at the same variant?
            //      If this is actually invalid VCF then it should perhaps throw an exception instead??
            if (fields.Length != values.Length) 
            {
                // There are missing values
                // DEV: For now suppress this warning
                //AuxillaryFunctions.WriteLog(@"Some genotype fields are missing values, blank values reported", parameters, errorCode: 1);
                // Add genotype column
                // First column is always GT
                formatSubFields.Add(fields[0], values[0]);
                // Ignore other columns
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
            return formatSubFields;
        }

        /// <summary>
        /// Parses the SNPEff EFF format annotation for a variant.
        /// </summary>
        /// <remarks>
        /// Modify to retrun the SnpEffAnnotations list instead of altering it at the class level?
        /// </remarks>
        private void ParseSnpEff4EFF()
        {
            string sequence_featureRgxString = @"^sequence_feature"; //skip these annotations
            Regex sequence_featureRgx = new Regex(sequence_featureRgxString);
            Annotation tempAnnotation = new Annotation();
            string effField;

            //iterate over SampleIDs
            foreach (KeyValuePair<string, List<VCFRecordWithGenotype>> iter in VCFRecords)
            {
                //iterate over variants
                foreach (VCFRecordWithGenotype record in iter.Value)
                {
                    // An EFF annotation is available for this variant
                    if (record.INFO.ContainsKey(@"EFF"))
                    {
                        //make genomic variant key
                        GenomicVariant tempGenomicVariant = new GenomicVariant(CHROM: record.CHROM, POS: record.POS, REF: record.REF, ALT: record.ALT);

                        //save eff field for lookups
                        effField = record.INFO[@"EFF"];

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

                            if (SnpEffAnnotations.ContainsKey(tempGenomicVariant) == true)
                            {
                                SnpEffAnnotations[tempGenomicVariant].Add(tempAnnotation);
                            }
                            else
                            {
                                SnpEffAnnotations.Add(tempGenomicVariant, new HashSet<Annotation>());
                                SnpEffAnnotations[tempGenomicVariant].Add(tempAnnotation);
                            }
                        }
                    }
                    else
                    {
                        // No EFF-format annotation found
                        // Move on to the next variant/sample
                        continue;
                    }
                }
            }
        }
    }
}
