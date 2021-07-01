using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;

namespace WRGLPipeline
{
    class GenotypingPipelineWrapper
    {
        // DEV: when used, replace this with the overall software version
        const double GenotypingPipelineVerison = 2.22;
        const double PipelineVersionForReport = 2.2;

        readonly private ParseSampleSheet sampleSheet;
        private string analysisDir, networkAnalysisDir, reportFilename;
        readonly private ProgrammeParameters parameters;
        readonly private List<BEDRecord> BEDRecords = new List<BEDRecord>();
        readonly private Dictionary<Tuple<string, string>, int> ampliconMinDP = new Dictionary<Tuple<string, string>, int>();
        readonly private Dictionary<string, Tuple<string, string>> fastqFileNames;
        private Int32 regiondepth;

        /// <summary>
        /// Wraps the genotyping analysis pipeline
        /// </summary>
        /// <param name="_sampleSheet">Parsed SampleSheet</param>
        /// <param name="_parameters">Configured ProgrammeParameters</param>
        /// <param name="_fastqFileNames">Dictionary of sample IDs and fastq file names</param>
        public GenotypingPipelineWrapper(ParseSampleSheet _sampleSheet, ProgrammeParameters _parameters, Dictionary<string, Tuple<string, string>> _fastqFileNames)
        {
            this.sampleSheet = _sampleSheet;
            this.parameters = _parameters;
            this.fastqFileNames = _fastqFileNames;

            ExecuteGenotypingPipeline();
        }

        /// <summary>
        /// Runs the genotyping pipeline
        /// </summary>
        private void ExecuteGenotypingPipeline()
        {
            // DEV: Should these folders be defined in the ProgrammeParameters?
            // Create the panel-specific analysis directories that are created within the run dir
            analysisDir = parameters.LocalFastqDir + @"\Genotyping_" + GenotypingPipelineVerison;
            networkAnalysisDir = parameters.NetworkRootRunDir + @"\Genotyping_" + GenotypingPipelineVerison;
            reportFilename = analysisDir + @"\" + parameters.RunID + @"_Genotyping_" + PipelineVersionForReport + ".report";

            AuxillaryFunctions.WriteLog(@"Starting genotyping pipeline...", parameters.LocalLogFilename, 0, false, parameters);
            AuxillaryFunctions.WriteLog(@"Variant report path: " + reportFilename, parameters.LocalLogFilename, 0, false, parameters);

            //create local output analysis directory
            try
            {
                Directory.CreateDirectory(analysisDir);
            } catch (Exception e)
            {
                AuxillaryFunctions.WriteLog(@"Could not create local Analysis directory: " + e.ToString(), parameters.LocalLogFilename, -1, false, parameters);
                throw;
            }

            //create network output analysis directory - if copy to Z option is set
            if (parameters.CopyToNetwork)
            {
                try
                {
                    Directory.CreateDirectory(networkAnalysisDir);
                }
                catch (Exception e)
                {
                    AuxillaryFunctions.WriteLog(@"Could not create network Analysis directory: " + e.ToString(), parameters.LocalLogFilename, -1, false, parameters);
                    throw;
                }
            }

            //write target region BED file for GATK
            GetGenotypingRegions();

            // Set the number of genotyping threads to keep within the maximum
            // Using Gemini required additional threads, so divide by this number
            int genotypingthreads = parameters.GenotypingMaxThreads;
            if ( parameters.GenotypingUseGemini)
            {
                // Since these are all integers, this should return an int without needing to round
                genotypingthreads = parameters.GenotypingMaxThreads / parameters.GeminiMultiThreads;
            }

            // Use Parallel.ForEach instead of ThreadPool - it may be more reliable
            // (and is certainly simpler!)
            // Run at most the number of threads defined in the .ini file
            Parallel.ForEach(sampleSheet.SampleRecords, new ParallelOptions { MaxDegreeOfParallelism = genotypingthreads }, (SampleRecord record) =>
            {
                if (record.Analysis == @"G")
                {
                    // Queue tasks for multithreading
                    // Use Task.Run() as this should stay within the limits of available threads
                    // i.e. it won't try to run 96 analyses in parallel - this kills the PC!
                    Console.WriteLine(@"Starting analysis for sample: " + record.Sample_ID.ToString());
                    GenerateGenotypingVCFs genotypeAnalysis = new GenerateGenotypingVCFs(record, analysisDir, parameters, sampleSheet, fastqFileNames[record.Sample_ID]);
                    genotypeAnalysis.MapReads();
                    Console.WriteLine(@"Analysis complete for sample: " + record.Sample_ID.ToString());
                 }
            });

            //call variants, annotate and tabulate
            GenerateGenotypingVCFs.CallSomaticVariants(analysisDir, parameters);
            WriteGenotypingReport();

            //copy files to network
            if (parameters.CopyToNetwork)
            {
                File.Copy(analysisDir + @"\GenotypingRegions.bed", networkAnalysisDir + @"\GenotypingRegions.bed");
                File.Copy(reportFilename, networkAnalysisDir + @"\" + parameters.RunID + @"_" + GenotypingPipelineVerison + ".report");
                File.Copy(reportFilename, parameters.GenotypingRepo + @"\" + Path.GetFileName(reportFilename));
                // DEV: This is never really used, and it was causing problems because Pisces doesn't make the same file
                //      Copys below weren't running because of this.
                //File.Copy(analysisDir + @"\VariantCallingLogs\SomaticVariantCallerLog.txt", networkAnalysisDir + @"\SomaticVariantCallerLog.txt");
                foreach (string file in Directory.GetFiles(analysisDir, @"*.log")) { File.Copy(file, networkAnalysisDir + @"\" + Path.GetFileName(file)); }
                foreach (string file in Directory.GetFiles(analysisDir, @"*.txt")) { File.Copy(file, networkAnalysisDir + @"\" + Path.GetFileName(file)); }
                foreach (string file in Directory.GetFiles(analysisDir, @"*.vcf")) { File.Copy(file, networkAnalysisDir + @"\" + Path.GetFileName(file)); }
            }

            // DEV: test if transferring the BAM files works nicely here
            if ((parameters.BamDownload) && (parameters.CopyToNetwork))
            {
                // Create the run folder in the Genotyping BAM store
                string RunBamStore = $@"{parameters.BamStoreLocation}\Genotyping\{parameters.RunID}";

                // Create the local BAM store folder (from Run ID)
                try
                {
                    System.IO.Directory.CreateDirectory(RunBamStore);
                }
                catch
                {
                    AuxillaryFunctions.WriteLog(@"Could not create local BAM file store folder", parameters.LocalLogFilename, -1, false, parameters);
                    throw;
                }

                foreach (string file in Directory.GetFiles(analysisDir, @"*.bam"))
                {
                    AuxillaryFunctions.WriteLog($@"Copy {Path.GetFileName(file)} to {parameters.BamStoreLocation}...", parameters.LocalLogFilename, 0, false, parameters);
                    File.Copy(file, $@"{RunBamStore}\{Path.GetFileName(file)}");
                }
                // TODO: See if we can do BAM and BAI together within the same loop
                foreach (string file in Directory.GetFiles(analysisDir, @"*.bai"))
                {
                    File.Copy(file, $@"{RunBamStore}\{Path.GetFileName(file)}");
                }
            }
            // DEV: Sending emails is currently not working
            //AuxillaryFunctions.SendRunCompletionEmail(logFilename, parameters.getGenotypingRepo + @"\" + Path.GetFileName(reportFilename), sampleSheet, @"Genotyping_" + GenotypingPipelineVerison, runID, parameters);
        }

        /// <summary>
        /// Write the local report file, summarising the variants detected in every sample.
        /// </summary>
        private void WriteGenotypingReport() //write output report
        {
            AuxillaryFunctions.WriteLog(@"Writing genotyping report...", parameters.LocalLogFilename, 0, false, parameters);
            StreamWriter genotypingReport = new StreamWriter(reportFilename);
            //GenomicVariant tempGenomicVariant;

            //make file of unique variatns passing QC
            GenerateGenotypingVCFs.CompressVariants(parameters, sampleSheet, analysisDir);

            //annotated variants
            Console.WriteLine($@"Analysis directory: {analysisDir}");
            ParseVCF annotatedVCFFile = GenerateGenotypingVCFs.CallSNPEff(parameters, analysisDir);

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
            foreach (SampleRecord sampleRecord in sampleSheet.SampleRecords)
            {
                //skip non-genotyping analyses
                if (sampleRecord.Analysis != "G"){
                    continue;
                }

                //parse VCF and bank entries
                string vcffile = Directory.GetFiles(analysisDir, sampleRecord.Sample_ID + @"*.vcf")[0];
                ParseVCF VCFFile = new ParseVCF(vcffile, parameters);

                //loop over VCF entries
                string sampleid = "SampleID";
                if (parameters.GenotypingUsePisces == true)
                {
                    sampleid = sampleRecord.Sample_ID + @".bam"; 
                }
                foreach (VCFRecordWithGenotype VCFrecord in VCFFile.VCFRecords[sampleid])
                {
                    //print variants that pass qc
                    if (VCFrecord.FILTER == @"PASS" && VCFrecord.QUAL >= 30 && int.Parse(VCFrecord.INFO[@"DP"]) >= 1000)
                    {
                        GenomicVariant tempGenomicVariant = new GenomicVariant(CHROM: VCFrecord.CHROM, REF: VCFrecord.REF, ALT: VCFrecord.ALT, POS: VCFrecord.POS);
                        //tempGenomicVariant.CHROM = VCFrecord.CHROM;
                        //tempGenomicVariant.POS = VCFrecord.POS;
                        //tempGenomicVariant.REF = VCFrecord.REF;
                        //tempGenomicVariant.ALT = VCFrecord.ALT;

                        Tuple<string, int> gTemp = new Tuple<string, int>(VCFrecord.CHROM, VCFrecord.POS);
                        string ampliconID = AuxillaryFunctions.LookupAmpliconID(gTemp, BEDRecords); //lookup variant amplicon
                        string[] ADFields = VCFrecord.FORMAT["AD"].Split(',');

                        if (ampliconMinDP[new Tuple<string, string>(sampleRecord.Sample_ID,ampliconID)] >= 1000) //amplicon has not failed; print variant
                        {
                            //add to mutant amplicon list
                            mutantAmplicons.Add(ampliconID);

                            //loop over annotations and print data
                            if (annotatedVCFFile.SnpEffAnnotations.ContainsKey(tempGenomicVariant) == true) //annotation available for this variant
                            {
                                //loop over annotations and print
                                foreach (Annotation ann in annotatedVCFFile.SnpEffAnnotations[tempGenomicVariant])
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
                                    genotypingReport.Write("\t" + VCFrecord.INFO["DP"]);
                                    genotypingReport.Write("\t" + ADFields[0]);
                                    genotypingReport.Write("\t" + ADFields[1]);
                                    genotypingReport.Write("\t" + (Convert.ToDouble(VCFrecord.FORMAT["VF"]) * 100) + '%');
                                    genotypingReport.Write("\t" + VCFrecord.FORMAT["NL"]); //Noise level
                                    genotypingReport.Write("\t" + VCFrecord.FORMAT["SB"]); //Strand bias
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
                                genotypingReport.Write("\t" + VCFrecord.INFO["DP"]);
                                genotypingReport.Write("\t" + ADFields[0]);
                                genotypingReport.Write("\t" + ADFields[1]);
                                genotypingReport.Write("\t" + (Convert.ToDouble(VCFrecord.FORMAT["VF"]) * 100) + '%');
                                genotypingReport.Write("\t" + VCFrecord.FORMAT["NL"]); //Noise level
                                genotypingReport.Write("\t" + VCFrecord.FORMAT["SB"]); //Strand bias
                                genotypingReport.WriteLine();
                            }
                        }
                    }
                } //done reading VCF

                //print Normal/Fail
                foreach (BEDRecord region in BEDRecords) //iterate over all amplicons
                {
                    try
                    {
                        regiondepth = ampliconMinDP[new Tuple<string, string>(sampleRecord.Sample_ID, region.Name)];
                    }
                    catch
                    {
                        regiondepth = 0;
                    }
                    if (mutantAmplicons.Contains(region.Name) == true)
                    {
                        continue; //skip mutant amplicons
                    }
                    else if (regiondepth < parameters.GenotypingDepth)  //this amplicon failed
                    {
                        genotypingReport.Write(sampleRecord.Sample_ID);
                        genotypingReport.Write("\t");
                        genotypingReport.Write(sampleRecord.Sample_Name);
                        genotypingReport.Write("\t" + region.Name);
                        genotypingReport.Write("\t" + GenotypingPipelineVerison);
                        genotypingReport.Write("\tFailed\t\t\t\t\t\t");
                        genotypingReport.Write(regiondepth);
                        genotypingReport.WriteLine();
                    }
                    else //normal
                    {
                        genotypingReport.Write(sampleRecord.Sample_ID);
                        genotypingReport.Write("\t");
                        genotypingReport.Write(sampleRecord.Sample_Name);
                        genotypingReport.Write("\t" + region.Name);
                        genotypingReport.Write("\t" + GenotypingPipelineVerison);
                        genotypingReport.Write("\tNo Mutation Detected\t\t\t\t\t\t");
                        genotypingReport.Write(regiondepth);
                        genotypingReport.WriteLine();
                    }

                }

                //reset mutant amplicons
                mutantAmplicons.Clear();

            } //done looping over samples

            genotypingReport.Close();
        }

        /// <summary>
        /// Generates a BED file from the AmpliconAligner input file
        /// </summary>
        private void GetGenotypingRegions() //output BED file for accessory programmes & write to memory
        {
            bool passedFirstHeader = false;
            string line;
            string[] fields;
            StreamReader ampliconAlignerV2Inputreader = new StreamReader(sampleSheet.Analyses["G"]);
            StreamWriter GenotypingRegionsBED = new StreamWriter(analysisDir + @"\GenotypingRegions.bed");

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
                        AuxillaryFunctions.WriteLog(@"AmpliconAligner input file is malformed. Check number of columns", parameters.LocalLogFilename, -1, false, parameters);
                        throw new FileLoadException();
                    }

                    int startPos;
                    int endPos;
                    int seqLen = fields[3].Length; //sequence length

                    // DEV: "checked" means that the wrapped code is protected against integer overflow
                    //      It's probably not necessary, as 32bit integers max out at 2,147,483,647 and there
                    //      are no human chromosomes that long. But maybe g. values could reach that high?
                    checked
                    {                      
                        startPos = int.Parse(fields[2]) - 1; //0-based start
                        endPos = startPos + seqLen;
                        // Start and end coordinates must be reversed if the region is defined on
                        // the negative strand
                        // DEV: shouls this check be added to the ParseBed class? Probably.
                        if (fields[6] == @"+")
                        {
                            startPos += int.Parse(fields[4]);
                            endPos -= int.Parse(fields[5]);
                        }
                        else
                        {
                            startPos += int.Parse(fields[5]);
                            endPos -= int.Parse(fields[4]);
                            
                        }

                    }

                    BEDRecord tempRecord = new BEDRecord(chromosome: fields[1],
                                                         start: startPos,
                                                         end: endPos,
                                                         name: fields[0]);
                    BEDRecords.Add(tempRecord);

                    GenotypingRegionsBED.Write(tempRecord.Chromosome);
                    GenotypingRegionsBED.Write("\t");
                    GenotypingRegionsBED.Write(tempRecord.Start);
                    GenotypingRegionsBED.Write("\t");
                    GenotypingRegionsBED.Write(tempRecord.End);
                    GenotypingRegionsBED.Write("\t");
                    GenotypingRegionsBED.Write(tempRecord.Name);
                    GenotypingRegionsBED.Write("\n");
                }
            }

            GenotypingRegionsBED.Close();

        }

        /// <summary>
        /// Read in the MappingStats file gnerated by the aligner for each sample
        /// </summary>
        private void AnalyseCoverageFromAlignerOutput()
        {
            AuxillaryFunctions.WriteLog(@"Calculating coverage values...", parameters.LocalLogFilename, 0, false, parameters);
            string line;

            //loop over sampleIDs
            foreach (SampleRecord record in sampleSheet.SampleRecords)
            {
                if (record.Analysis != "G")
                {
                    continue;
                }
                // Read the MappingStats file
                using (FileStream stream = new FileStream(analysisDir + @"\" + record.Sample_ID + @"_MappingStats.txt", FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (StreamReader ampliconAlignerStatsFile = new StreamReader(stream))
                    {
                        while ((line = ampliconAlignerStatsFile.ReadLine()) != null)
                        {
                            if (line == "" || line[0] == '#')
                            {
                                continue;
                            }
                            else
                            {
                                string[] fields = line.Split('\t');
                                ampliconMinDP.Add(new Tuple<string, string>(record.Sample_ID, fields[0]), int.Parse(fields[3])); //mappedReads
                            }
                        }
                    }
                }
            }
        }
    }
}