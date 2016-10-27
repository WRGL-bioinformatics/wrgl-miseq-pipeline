using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WRGLPipeline
{
    class GenotypingPipelineWrapper
    {
        const double GenotypingPipelineVerison = 2.1;

        private ParseSampleSheet sampleSheet;
        private string localFastqDir, logFilename, runID, networkRootRunDir, analysisDir, networkAnalysisDir, reportFilename;
        private ProgrammeParameters parameters;
        private List<BEDRecord> BEDRecords = new List<BEDRecord>();
        private Dictionary<Tuple<string, string>, UInt32> ampliconMinDP = new Dictionary<Tuple<string, string>, UInt32>();
        private Dictionary<string, Tuple<string, string>> fastqFileNames;

        public GenotypingPipelineWrapper(ParseSampleSheet _sampleSheet, string _localFastqDir, string _logFilename, string _runID, ProgrammeParameters _parameters, string _networkRootRunDir, Dictionary<string, Tuple<string, string>> _fastqFileNames)
        {
            this.sampleSheet = _sampleSheet;
            this.localFastqDir = _localFastqDir;
            this.logFilename = _logFilename;
            this.runID = _runID;
            this.parameters = _parameters;
            this.networkRootRunDir = _networkRootRunDir;
            this.fastqFileNames = _fastqFileNames;

            ExecuteGenotypingPipeline();
        }

        private void ExecuteGenotypingPipeline()
        {
            analysisDir = localFastqDir + @"\Genotyping_" + GenotypingPipelineVerison;
            networkAnalysisDir = networkRootRunDir + @"\Genotyping_" + GenotypingPipelineVerison;
            reportFilename = analysisDir + @"\" + runID + @"_Genotyping_" + GenotypingPipelineVerison + ".report";

            AuxillaryFunctions.WriteLog(@"Starting genotyping pipeline...", logFilename, 0, false, parameters);
            AuxillaryFunctions.WriteLog(@"Variant report path: " + reportFilename, logFilename, 0, false, parameters);

            //create local output analysis directory
            try { Directory.CreateDirectory(analysisDir); } catch (Exception e) { AuxillaryFunctions.WriteLog(@"Could not create local Analysis directory: " + e.ToString(), logFilename, -1, false, parameters);
                throw;
            }

            //create network output analysis directory
            try { Directory.CreateDirectory(networkAnalysisDir); } catch (Exception e) { AuxillaryFunctions.WriteLog(@"Could not create network Analysis directory: " + e.ToString(), logFilename, -1, false, parameters);
                throw;
            }

            //write target region BED file for GATK
            GetGenotypingRegions();

            //map and realign around indels
            List<Task> threadTasks = new List<Task>();
            foreach (SampleRecord record in sampleSheet.getSampleRecords)
            {
                if (record.Analysis == @"G")
                {
                    GenerateGenotypingVCFs genotypeAnalysis = new GenerateGenotypingVCFs(record, analysisDir, logFilename, parameters, sampleSheet, fastqFileNames[record.Sample_ID]);

                    //queue tasks for multithreading
                    threadTasks.Add(Task.Factory.StartNew(() =>
                    {
                        genotypeAnalysis.MapReads();
                    }));
                }
            }

            //wait for jobs to finish
            Task.WaitAll(threadTasks.ToArray());

            //call variants, annotate and tabulate
            GenerateGenotypingVCFs.CallSomaticVariants(analysisDir, parameters);
            WriteGenotypingReport();

            //copy files to network
            File.Copy(analysisDir + @"\GenotypingRegions.bed", networkAnalysisDir + @"\GenotypingRegions.bed");
            File.Copy(reportFilename, networkAnalysisDir + @"\" + runID + @"_" + GenotypingPipelineVerison + ".report");
            File.Copy(reportFilename, parameters.getGenotypingRepo + @"\" + Path.GetFileName(reportFilename));
            File.Copy(analysisDir + @"\VariantCallingLogs\SomaticVariantCallerLog.txt", networkAnalysisDir + @"\SomaticVariantCallerLog.txt");
            foreach (string file in Directory.GetFiles(analysisDir, @"*.log")) { File.Copy(file, networkAnalysisDir + @"\" + Path.GetFileName(file)); }
            foreach (string file in Directory.GetFiles(analysisDir, @"*.txt")) { File.Copy(file, networkAnalysisDir + @"\" + Path.GetFileName(file)); }
            foreach (string file in Directory.GetFiles(analysisDir, @"*.vcf")) { File.Copy(file, networkAnalysisDir + @"\" + Path.GetFileName(file)); }

            AuxillaryFunctions.SendRunCompletionEmail(logFilename, parameters.getGenotypingRepo + @"\" + Path.GetFileName(reportFilename), sampleSheet, @"Genotyping_" + GenotypingPipelineVerison, runID, parameters);
        }

        private void WriteGenotypingReport() //write output report
        {
            AuxillaryFunctions.WriteLog(@"Writing genotyping report...", logFilename, 0, false, parameters);
            StreamWriter genotypingReport = new StreamWriter(reportFilename);
            GenomicVariant tempGenomicVariant;

            //make file of unique variatns passing QC
            GenerateGenotypingVCFs.CompressVariants(logFilename, parameters, sampleSheet, analysisDir);

            //annotated variants
            ParseVCF annotatedVCFFile = GenerateGenotypingVCFs.CallSNPEff(logFilename, parameters, analysisDir);

            //get minimum depth for each amplicon
            AnalyseCoverageFromAlignerOutput();

            //record mutant amplicons
            HashSet<string> mutantAmplicons = new HashSet<string>();

            //write column headers
            genotypingReport.Write("Sample_ID\t");
            genotypingReport.Write("Sample_Name\t");
            genotypingReport.Write("Amplicon\t");
            genotypingReport.Write("Pipeline\t");
            genotypingReport.Write("Result\t");
            genotypingReport.Write("Chromosome\t");
            genotypingReport.Write("Position\t");
            genotypingReport.Write("ReferenceBase\t");
            genotypingReport.Write("AlternativeBase\t");
            genotypingReport.Write("Quality\t");
            genotypingReport.Write("Depth\t");
            genotypingReport.Write("ReferenceDepth\t");
            genotypingReport.Write("AlternativeDepth\t");
            genotypingReport.Write("VariantFrequency\t");
            genotypingReport.Write("NoiseLevel\t");
            genotypingReport.Write("StranBias\t");
            genotypingReport.Write("Transcript\t");
            genotypingReport.Write("Gene\t");
            genotypingReport.Write("HGVSc\t");
            genotypingReport.Write("HGVSp\t");
            genotypingReport.Write("Exon\t");
            genotypingReport.Write("Consequence");
            genotypingReport.WriteLine();

            //loop over SampleSheet records
            foreach (SampleRecord sampleRecord in sampleSheet.getSampleRecords)
            {
                //skip non-genotyping analyses
                if (sampleRecord.Analysis != "G"){
                    continue;
                }

                //parse VCF and bank entries
                ParseVCF VCFFile = new ParseVCF(analysisDir + @"\" + sampleRecord.Sample_ID + @"_S999.vcf", logFilename, parameters);

                //loop over VCF entries
                foreach (VCFRecordWithGenotype VCFrecord in VCFFile.getVCFRecords["SampleID"])
                {
                    //print variants that pass qc
                    if (VCFrecord.FILTER == @"PASS" && VCFrecord.QUAL >= 30 && Convert.ToUInt32(VCFrecord.infoSubFields[@"DP"]) >= 1000)
                    {
                        tempGenomicVariant.CHROM = VCFrecord.CHROM;
                        tempGenomicVariant.POS = VCFrecord.POS;
                        tempGenomicVariant.REF = VCFrecord.REF;
                        tempGenomicVariant.ALT = VCFrecord.ALT;

                        Tuple<string, UInt32> gTemp = new Tuple<string, UInt32>(VCFrecord.CHROM, VCFrecord.POS);
                        string ampliconID = AuxillaryFunctions.LookupAmpliconID(gTemp, BEDRecords); //lookup variant amplicon
                        string[] ADFields = VCFrecord.formatSubFields["AD"].Split(',');

                        if (ampliconMinDP[new Tuple<string, string>(sampleRecord.Sample_ID,ampliconID)] >= 1000) //amplicon has not failed; print variant
                        {
                            //add to mutant amplicon list
                            mutantAmplicons.Add(ampliconID);

                            //loop over annotations and print data
                            if (annotatedVCFFile.getSnpEffAnnotations.ContainsKey(tempGenomicVariant) == true) //annotation available for this variant
                            {
                                //loop over annotations and print
                                foreach (Annotation ann in annotatedVCFFile.getSnpEffAnnotations[tempGenomicVariant])
                                {
                                    //split HGVSc & p
                                    string[] HGVS = ann.Amino_Acid_Change.Split('/');

                                    genotypingReport.Write(sampleRecord.Sample_ID);
                                    genotypingReport.Write("\t");
                                    genotypingReport.Write(sampleRecord.Sample_Name);
                                    genotypingReport.Write("\t");
                                    genotypingReport.Write(ampliconID);
                                    genotypingReport.Write("\t" + GenotypingPipelineVerison);
                                    genotypingReport.Write("\tVariant");
                                    genotypingReport.Write("\t" + VCFrecord.CHROM);
                                    genotypingReport.Write("\t" + VCFrecord.POS);
                                    genotypingReport.Write("\t" + VCFrecord.REF);
                                    genotypingReport.Write("\t" + VCFrecord.ALT);
                                    genotypingReport.Write("\t" + VCFrecord.QUAL);
                                    genotypingReport.Write("\t" + VCFrecord.infoSubFields["DP"]);
                                    genotypingReport.Write("\t" + ADFields[0]);
                                    genotypingReport.Write("\t" + ADFields[1]);
                                    genotypingReport.Write("\t" + (Convert.ToDouble(VCFrecord.formatSubFields["VF"]) * 100) + '%');
                                    genotypingReport.Write("\t" + VCFrecord.formatSubFields["NL"]); //Noise level
                                    genotypingReport.Write("\t" + VCFrecord.formatSubFields["SB"]); //Strand bias
                                    genotypingReport.Write("\t" + ann.Transcript_ID);
                                    genotypingReport.Write("\t" + ann.Gene_Name);

                                    if (HGVS.Length > 1){ //both c. & p. are available
                                        genotypingReport.Write("\t" + HGVS[1]);
                                        genotypingReport.Write("\t" + HGVS[0]);
                                    }
                                    else
                                    {
                                        genotypingReport.Write("\t" + HGVS[0]);
                                        genotypingReport.Write("\t");
                                    }

                                    genotypingReport.Write("\t" + ann.Exon_Rank);
                                    genotypingReport.Write("\t" + ann.Effect);
                                    genotypingReport.WriteLine();
                                }

                            } else { //print without annotations
                                genotypingReport.Write(sampleRecord.Sample_ID);
                                genotypingReport.Write("\t");
                                genotypingReport.Write(sampleRecord.Sample_Name);
                                genotypingReport.Write("\t");
                                genotypingReport.Write(ampliconID);
                                genotypingReport.Write("\t" + GenotypingPipelineVerison);
                                genotypingReport.Write("\tVariant");
                                genotypingReport.Write("\t" + VCFrecord.CHROM);
                                genotypingReport.Write("\t" + VCFrecord.POS);
                                genotypingReport.Write("\t" + VCFrecord.REF);
                                genotypingReport.Write("\t" + VCFrecord.ALT);
                                genotypingReport.Write("\t" + VCFrecord.QUAL);
                                genotypingReport.Write("\t" + VCFrecord.infoSubFields["DP"]);
                                genotypingReport.Write("\t" + ADFields[0]);
                                genotypingReport.Write("\t" + ADFields[1]);
                                genotypingReport.Write("\t" + (Convert.ToDouble(VCFrecord.formatSubFields["VF"]) * 100) + '%');
                                genotypingReport.Write("\t" + VCFrecord.formatSubFields["NL"]); //Noise level
                                genotypingReport.Write("\t" + VCFrecord.formatSubFields["SB"]); //Strand bias
                                genotypingReport.WriteLine();
                            }
                        }
                    }
                } //done reading VCF

                //print Normal/Fail
                foreach (BEDRecord region in BEDRecords) //iterate over all amplicons
                {
                    if (mutantAmplicons.Contains(region.name) == true)
                    {
                        continue; //skip mutant amplicons
                    }
                    else if (ampliconMinDP[new Tuple<string, string>(sampleRecord.Sample_ID, region.name)] < 1000)  //this amplicon failed
                    {
                        genotypingReport.Write(sampleRecord.Sample_ID);
                        genotypingReport.Write("\t");
                        genotypingReport.Write(sampleRecord.Sample_Name);
                        genotypingReport.Write("\t" + region.name);
                        genotypingReport.Write("\t" + GenotypingPipelineVerison);
                        genotypingReport.Write("\tFailed\t\t\t\t\t\t");
                        genotypingReport.Write(ampliconMinDP[new Tuple<string, string>(sampleRecord.Sample_ID, region.name)]);
                        genotypingReport.WriteLine();
                    }
                    else //normal
                    {
                        genotypingReport.Write(sampleRecord.Sample_ID);
                        genotypingReport.Write("\t");
                        genotypingReport.Write(sampleRecord.Sample_Name);
                        genotypingReport.Write("\t" + region.name);
                        genotypingReport.Write("\t" + GenotypingPipelineVerison);
                        genotypingReport.Write("\tNo Mutation Detected\t\t\t\t\t\t");
                        genotypingReport.Write(ampliconMinDP[new Tuple<string, string>(sampleRecord.Sample_ID, region.name)]);
                        genotypingReport.WriteLine();
                    }

                }

                //reset mutant amplicons
                mutantAmplicons.Clear();

            } //done looping over samples

            genotypingReport.Close();
        }

        private void GetGenotypingRegions() //output BED file for accessory programmes & write to memory
        {
            bool passedFirstHeader = false;
            string line;
            string[] fields;
            StreamReader ampliconAlignerV2Inputreader = new StreamReader(sampleSheet.getAnalyses["G"]);
            StreamWriter GenotypingRegionsBED = new StreamWriter(analysisDir + @"\GenotypingRegions.bed");
            BEDRecord tempRecord;

            while ((line = ampliconAlignerV2Inputreader.ReadLine()) != null)
            {
                if (line != "")
                {
                    if (line[0] == '#'){

                        if (passedFirstHeader == false)
                        {
                            passedFirstHeader = true;
                            continue;
                        }
                        else
                        {
                            break;
                        }
                    }

                    fields = line.Split('\t');

                    if (fields.Length != 7)
                    {
                        AuxillaryFunctions.WriteLog(@"AmpliconAligner input file is malformed. Check number of columns", logFilename, -1, false, parameters);
                        throw new FileLoadException();
                    }

                    tempRecord.chromosome = fields[1];
                    tempRecord.name = fields[0];

                    checked
                    {
                        UInt32 startPos;
                        UInt32 endPos;
                        UInt32 seqLen = Convert.ToUInt32(fields[3].Length); //sequencne length
                        
                        startPos = Convert.ToUInt32(fields[2]) - 1; //0-based start
                        endPos = startPos + seqLen;

                        if (fields[6] == @"+")
                        {
                            tempRecord.start = startPos + Convert.ToUInt32(fields[4]);
                            tempRecord.end = endPos - Convert.ToUInt32(fields[5]);
                        }
                        else
                        {
                            tempRecord.start = startPos + Convert.ToUInt32(fields[5]);
                            tempRecord.end = endPos - Convert.ToUInt32(fields[4]);
                        }

                    }

                    BEDRecords.Add(tempRecord);

                    GenotypingRegionsBED.Write(tempRecord.chromosome);
                    GenotypingRegionsBED.Write("\t");
                    GenotypingRegionsBED.Write(tempRecord.start);
                    GenotypingRegionsBED.Write("\t");
                    GenotypingRegionsBED.Write(tempRecord.end);
                    GenotypingRegionsBED.Write("\t");
                    GenotypingRegionsBED.Write(tempRecord.name);
                    GenotypingRegionsBED.Write("\n");
                }
            }

            GenotypingRegionsBED.Close();

        }

        private void AnalyseCoverageFromAlignerOutput()
        {
            AuxillaryFunctions.WriteLog(@"Calculating coverage values...", logFilename, 0, false, parameters);
            string line;

            //loop over sampleIDs
            foreach (SampleRecord record in sampleSheet.getSampleRecords)
            {
                if (record.Analysis != "G")
                {
                    continue;
                }

                StreamReader ampliconAlignerStatsFile = new StreamReader(analysisDir + @"\" + record.Sample_ID + @"_MappingStats.txt");

                while ((line = ampliconAlignerStatsFile.ReadLine()) != null)
                {
                    if (line == ""){
                        continue;
                    } else if (line[0] == '#'){
                        continue;
                    }
                    else
                    {
                        string[] fields = line.Split('\t');
                        ampliconMinDP.Add(new Tuple<string, string>(record.Sample_ID, fields[0]), Convert.ToUInt32(fields[3])); //mappedReads
                    }
                }

                ampliconAlignerStatsFile.Close();
            }

        }

        private void AnalyseCoverageData()
        {
            AuxillaryFunctions.WriteLog(@"Calculating coverage values...", logFilename, 0, false, parameters);

            UInt32 pos;
            string line, ampliconID;
            List<string> sampleIDs = new List<string>();
            Dictionary<Tuple<string, UInt32>, bool> isBaseCovered = new Dictionary<Tuple<string, UInt32>, bool>(); //bool = observed
            StringBuilder samtoolsDepthParameter = new StringBuilder();

            samtoolsDepthParameter.Append(@"depth ");
            samtoolsDepthParameter.Append(@"-q ");
            samtoolsDepthParameter.Append(20);
            samtoolsDepthParameter.Append(' ');
            samtoolsDepthParameter.Append(@"-Q ");
            samtoolsDepthParameter.Append(0);
            samtoolsDepthParameter.Append(' ');
            samtoolsDepthParameter.Append(@"-b ");
            samtoolsDepthParameter.Append(analysisDir + @"\GenotypingRegions.bed");
            samtoolsDepthParameter.Append(' ');

            //loop over sampleIDs
            foreach (SampleRecord record in sampleSheet.getSampleRecords)
            {
                if (record.Analysis != "G")
                {
                    continue;
                }

                samtoolsDepthParameter.Append(analysisDir);
                samtoolsDepthParameter.Append(@"\");
                samtoolsDepthParameter.Append(record.Sample_ID);
                samtoolsDepthParameter.Append(@".bam ");

                //loop over amplicons and initalise dicitonary
                foreach (BEDRecord amplicon in BEDRecords)
                {
                    ampliconMinDP.Add(new Tuple<string, string>(record.Sample_ID, amplicon.name), UInt32.MaxValue); //initalise with maxValue
                }

                sampleIDs.Add(record.Sample_ID);
            }

            //loop over target ROI, hash bases
            foreach (BEDRecord record in BEDRecords)
            {
                //iterate over region
                for (pos = record.start + 2; pos < record.end + 1; ++pos)
                {
                    if (!isBaseCovered.ContainsKey(new Tuple<string, UInt32>(record.chromosome, pos)))
                    {
                        isBaseCovered.Add(new Tuple<string, UInt32>(record.chromosome, pos), false);
                    }
                }

            }

            //annotated variants
            Process samtoolsDepth = new Process();
            samtoolsDepth.StartInfo.FileName = parameters.getSamtoolsPath;
            samtoolsDepth.StartInfo.Arguments = samtoolsDepthParameter.ToString();
            samtoolsDepth.StartInfo.UseShellExecute = false;
            samtoolsDepth.StartInfo.RedirectStandardOutput = true;
            samtoolsDepth.StartInfo.RedirectStandardError = true;
            samtoolsDepth.Start();

            string samtoolsDepthOutput = samtoolsDepth.StandardOutput.ReadToEnd();

            samtoolsDepth.WaitForExit();
            samtoolsDepth.Close();

            using (StringReader reader = new StringReader(samtoolsDepthOutput))
            {
                // Loop over the lines in the string.
                while ((line = reader.ReadLine()) != null)
                {
                    string[] fields = line.Split('\t');
                    pos = Convert.ToUInt32(fields[1]);

                    //mark base as observed in the dataset
                    isBaseCovered[new Tuple<string, UInt32>(fields[0], pos)] = true;

                    for (int n = 2; n < fields.Length; ++n) //skip chrom & pos
                    {
                        //mark amplicon as failed
                        ampliconID = AuxillaryFunctions.LookupAmpliconID(new Tuple<string, UInt32>(fields[0], pos), BEDRecords);

                        if (ampliconID == "")
                        {
                            break;  
                        }
                        
                        if (ampliconMinDP[new Tuple<string, string>(sampleIDs[n - 2], ampliconID)] > Convert.ToUInt32(fields[n]))
                        {
                            ampliconMinDP[new Tuple<string, string>(sampleIDs[n - 2], ampliconID)] = Convert.ToUInt32(fields[n]);
                        }

                    }
                }
            }

            //reset max val for missing data
            foreach (KeyValuePair<Tuple<string, UInt32>, bool> chromBase in isBaseCovered){
                if (chromBase.Value == false){ //base not seen in data
                    ampliconID = AuxillaryFunctions.LookupAmpliconID(new Tuple<string, UInt32>(chromBase.Key.Item1, chromBase.Key.Item2), BEDRecords);

                    //loop over sampleIDs and reset to 0
                    foreach (SampleRecord record in sampleSheet.getSampleRecords) {
                        ampliconMinDP[new Tuple<string, string>(record.Sample_ID, ampliconID)] = 0;
                    }

                }
            }
        }
    }
}
