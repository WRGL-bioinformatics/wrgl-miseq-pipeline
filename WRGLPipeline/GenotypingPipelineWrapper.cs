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
        /// <param name="sampleSheet">Parsed SampleSheet</param>
        /// <param name="parameters">Configured ProgrammeParameters</param>
        /// <param name="fastqFileNames">Dictionary of sample IDs and fastq file names</param>
        public GenotypingPipelineWrapper(ParseSampleSheet sampleSheet, ProgrammeParameters parameters, Dictionary<string, Tuple<string, string>> fastqFileNames)
        {
            this.parameters = parameters;
            this.sampleSheet = sampleSheet;
            this.fastqFileNames = fastqFileNames;

            ExecuteGenotypingPipeline(parameters);
        }

        /// <summary>
        /// Runs the genotyping pipeline
        /// </summary>
        private void ExecuteGenotypingPipeline(ProgrammeParameters parameters)
        {
            // Create the panel-specific analysis directories that are created within the run dir
            analysisDir = $@"{parameters.LocalFastqDir}\Genotyping_{parameters.GenotypingAnalysisVersion}";
            networkAnalysisDir = $@"{parameters.NetworkRootRunDir}\Genotyping_{parameters.GenotypingAnalysisVersion}";

            // DEV: I think the ideal thing to do is remove the version, and possibly add it in the report itself?
            //      That would remove issues with the file not being found by other processes after an update...
            //      Although I'd *thought* we'd added wildcards into the relevant Excel macros...
            reportFilename = $@"{analysisDir}\{parameters.RunID}_Genotyping_{parameters.PipelineVersionForReport}.report";

            AuxillaryFunctions.WriteLog(@"Starting genotyping pipeline...", parameters);
            AuxillaryFunctions.WriteLog($@"Variant report path: {reportFilename}", parameters);

            // Create the local output analysis directory
            try
            {
                Directory.CreateDirectory(analysisDir);
            } catch (Exception e)
            {
                AuxillaryFunctions.WriteLog($@"Could not create local Analysis directory: {e.ToString()}", parameters, errorCode: -1);
                throw;
            }

            // Create network output analysis directory - if copy to Z option is set
            if (parameters.CopyToNetwork)
            {
                try
                {
                    Directory.CreateDirectory(networkAnalysisDir);
                }
                catch (Exception e)
                {
                    AuxillaryFunctions.WriteLog($@"Could not create network Analysis directory: {e.ToString()}", parameters, errorCode: -1);
                    throw;
                }
            }

            // Write target region BED file for GATK
            GetGenotypingRegions();

            // Set the number of genotyping threads to keep within the maximum
            // Using Gemini required additional threads, so divide by this number
            int genotypingthreads = parameters.GenotypingMaxThreads;
            if ( parameters.GenotypingUseGemini)
            {
                // Since these are all integers, this should return an int without needing to round
                genotypingthreads = parameters.GenotypingMaxThreads / parameters.GeminiMultiThreads;
            }

            // Run at most the number of threads defined in the .ini file
            Parallel.ForEach(sampleSheet.SampleRecords, new ParallelOptions { MaxDegreeOfParallelism = genotypingthreads }, (SampleRecord record) =>
            {
                if (record.Analysis == @"G")
                {
                    // Queue tasks for multithreading
                    // Use Task.Run() as this should stay within the limits of available threads
                    // i.e. it won't try to run 96 analyses in parallel - this kills the PC!
                    Console.WriteLine($@"Starting analysis for sample: {record.Sample_ID.ToString()}");
                    GenerateGenotypingVCFs genotypeAnalysis = new GenerateGenotypingVCFs(record, analysisDir, parameters, sampleSheet, fastqFileNames[record.Sample_ID]);
                    genotypeAnalysis.MapReads();
                    Console.WriteLine($@"Analysis complete for sample: {record.Sample_ID.ToString()}");
                 }
            });

            // Call variants, annotate and tabulate
            GenerateGenotypingVCFs.CallSomaticVariants(analysisDir, parameters);
            WriteGenotypingReport(parameters);

            // Copy files to network
            if (parameters.CopyToNetwork)
            {
                File.Copy($@"{analysisDir}\GenotypingRegions.bed", $@"{networkAnalysisDir}\GenotypingRegions.bed");
                File.Copy(reportFilename, $@"{networkAnalysisDir}\{parameters.RunID}_{parameters.GenotypingAnalysisVersion}.report");
                File.Copy(reportFilename, $@"{parameters.GenotypingRepo}\{Path.GetFileName(reportFilename)}");
                foreach (string file in Directory.GetFiles(analysisDir, @"*.log")) { File.Copy(file, $@"{networkAnalysisDir}\{Path.GetFileName(file)}"); }
                foreach (string file in Directory.GetFiles(analysisDir, @"*.txt")) { File.Copy(file, $@"{networkAnalysisDir}\{Path.GetFileName(file)}"); }
                foreach (string file in Directory.GetFiles(analysisDir, @"*.vcf")) { File.Copy(file, $@"{networkAnalysisDir}\{Path.GetFileName(file)}"); }
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
                    AuxillaryFunctions.WriteLog(@"Could not create local BAM file store folder", parameters, errorCode: -1);
                    throw;
                }

                // DEV: since it supports wildcards, the following should allow us to do
                //      BAMs and indexes in the same loop (simpler!)
                //foreach (string file in Directory.GetFiles(analysisDir, @"*.ba*"))
                foreach (string file in Directory.GetFiles(analysisDir, @"*.bam"))
                {
                    AuxillaryFunctions.WriteLog($@"Copy {Path.GetFileName(file)} to {parameters.BamStoreLocation}...", parameters);
                    File.Copy(file, $@"{RunBamStore}\{Path.GetFileName(file)}");
                }
                // TODO: See if we can do BAM and BAI together within the same loop
                foreach (string file in Directory.GetFiles(analysisDir, @"*.bai"))
                {
                    File.Copy(file, $@"{RunBamStore}\{Path.GetFileName(file)}");
                }
            }
            // DEV: Sending emails is currently not working
            //AuxillaryFunctions.SendRunCompletionEmail(parameters.LocalLogFilename, $@"{parameters.GenotypingRepo}\{Path.GetFileName(reportFilename)}", sampleSheet, $@"Genotyping_{parameters.GenotypingAnalysisVersion}", parameters.RunID, parameters);
        }

        /// <summary>
        /// Write the local report file, summarising the variants detected in every sample.
        /// </summary>
        private void WriteGenotypingReport(ProgrammeParameters parameters) //write output report
        {
            AuxillaryFunctions.WriteLog(@"Writing genotyping report...", parameters);

            // Make file of unique variants passing QC
            GenerateGenotypingVCFs.CompressVariants(parameters, sampleSheet, analysisDir);

            // Annotated variants
            Console.WriteLine($@"Analysis directory: {analysisDir}");
            ParseVCF annotatedVCFFile = GenerateGenotypingVCFs.CallSNPEff(parameters, analysisDir);

            // Get minimum depth for each amplicon
            AnalyseCoverageFromAlignerOutput();

            // Record mutant amplicons
            HashSet<string> mutantAmplicons = new HashSet<string>();

            using (StreamWriter genotypingReport = new StreamWriter(reportFilename))
            {
                // Write column headers (split out as one long string would be off the screen)
                StringBuilder colHeaders = new StringBuilder();
                colHeaders.Append("Sample_ID\t");
                colHeaders.Append("Sample_Name\t");
                colHeaders.Append("Amplicon\t");
                colHeaders.Append("Pipeline\t");
                colHeaders.Append("Result\t");
                colHeaders.Append("Chromosome\t");
                colHeaders.Append("Position\t");
                colHeaders.Append("ReferenceBase\t");
                colHeaders.Append("AlternativeBase\t");
                colHeaders.Append("Quality\t");
                colHeaders.Append("Depth\t");
                colHeaders.Append("ReferenceDepth\t");
                colHeaders.Append("AlternativeDepth\t");
                colHeaders.Append("VariantFrequency\t");
                colHeaders.Append("NoiseLevel\t");
                colHeaders.Append("StranBias\t");
                colHeaders.Append("Transcript\t");
                colHeaders.Append("Gene\t");
                colHeaders.Append("HGVSc\t");
                colHeaders.Append("HGVSp\t");
                colHeaders.Append("Exon\t");
                colHeaders.Append("Consequence");

                genotypingReport.WriteLine(colHeaders.ToString());

                // Loop over SampleSheet records
                foreach (SampleRecord sampleRecord in sampleSheet.SampleRecords)
                {
                    // Skip non-genotyping analyses
                    if (sampleRecord.Analysis != "G")
                    {
                        continue;
                    }

                    // Parse VCF and bank entries
                    string vcffile = Directory.GetFiles(analysisDir, $@"{sampleRecord.Sample_ID}*.vcf")[0];
                    ParseVCF VCFFile = new ParseVCF(vcffile, parameters);

                    // Loop over VCF entries
                    string sampleid = "SampleID";
                    if (parameters.GenotypingUsePisces == true)
                    {
                        sampleid = sampleRecord.Sample_ID + @".bam";
                    }
                    foreach (VCFRecordWithGenotype VCFrecord in VCFFile.VCFRecords[sampleid])
                    {
                        // Only print variants that pass qc limits
                        // DEV: These should not be hard coded!
                        if (VCFrecord.FILTER == @"PASS" && VCFrecord.QUAL >= 30 && int.Parse(VCFrecord.INFO[@"DP"]) >= 1000)
                        {
                            GenomicVariant tempGenomicVariant = new GenomicVariant(CHROM: VCFrecord.CHROM, REF: VCFrecord.REF, ALT: VCFrecord.ALT, POS: VCFrecord.POS);
                            Tuple<string, int> gTemp = new Tuple<string, int>(VCFrecord.CHROM, VCFrecord.POS);

                            // Lookup variant amplicon
                            string ampliconID = AuxillaryFunctions.LookupAmpliconID(gTemp, BEDRecords);
                            string[] ADFields = VCFrecord.FORMAT["AD"].Split(',');

                            // Amplicon has not failed; print variant
                            if (ampliconMinDP[new Tuple<string, string>(sampleRecord.Sample_ID, ampliconID)] >= 1000)
                            {
                                // Add to mutant amplicon list
                                mutantAmplicons.Add(ampliconID);
                                                              
                                // Loop over annotations and print data
                                if (annotatedVCFFile.SnpEffAnnotations.ContainsKey(tempGenomicVariant) == true) //annotation available for this variant
                                {
                                    // Loop over annotations and print
                                    foreach (Annotation ann in annotatedVCFFile.SnpEffAnnotations[tempGenomicVariant])
                                    {
                                        // Split HGVS c. & p. descriptions
                                        string[] HGVS = ann.Amino_Acid_Change.Split('/');
                                        // If there's no p. there's only on field, so we need to add a placeholder
                                        // This happens for n. numbering, for example. The HGVS array must be resized first.
                                        if (HGVS.Length == 1)
                                        {
                                            Array.Resize(ref HGVS, HGVS.Length + 1);
                                            // The results are given reversed, so we have to switch in order
                                            // to print c. before p.
                                            HGVS[1] = HGVS[0];
                                            HGVS[0] = "";
                                        }

                                        genotypingReport.Write($"{sampleRecord.Sample_ID}\t");
                                        genotypingReport.Write($"{sampleRecord.Sample_Name}\t");
                                        genotypingReport.Write($"{ampliconID}\t");
                                        genotypingReport.Write($"{parameters.GenotypingAnalysisVersion}\t");
                                        genotypingReport.Write($"Variant\t");
                                        genotypingReport.Write($"{VCFrecord.CHROM}\t");
                                        genotypingReport.Write($"{VCFrecord.POS}\t");
                                        genotypingReport.Write($"{VCFrecord.REF}\t");
                                        genotypingReport.Write($"{VCFrecord.ALT}\t");
                                        genotypingReport.Write($"{VCFrecord.QUAL}\t");
                                        genotypingReport.Write($"{VCFrecord.INFO["DP"]}\t");
                                        genotypingReport.Write($"{ADFields[0]}\t");
                                        genotypingReport.Write($"{ADFields[1]}\t");
                                        genotypingReport.Write($"{(Convert.ToDouble(VCFrecord.FORMAT["VF"]) * 100)}%\t");
                                        genotypingReport.Write($"{VCFrecord.FORMAT["NL"]}\t");
                                        genotypingReport.Write($"{VCFrecord.FORMAT["SB"]}\t");
                                        genotypingReport.Write($"{ann.Transcript_ID}\t");
                                        genotypingReport.Write($"{ann.Gene_Name}\t");
                                        genotypingReport.Write($"{HGVS[1]}\t{HGVS[0]}\t");
                                        genotypingReport.WriteLine($"{ann.Exon_Rank}\t{ann.Effect}");
                                    }
                                }
                                else
                                {
                                    // Print without annotations
                                    genotypingReport.Write($"{sampleRecord.Sample_ID}\t");
                                    genotypingReport.Write($"{sampleRecord.Sample_Name}\t");
                                    genotypingReport.Write($"{ampliconID}\t");
                                    genotypingReport.Write($"{parameters.GenotypingAnalysisVersion}\t");
                                    genotypingReport.Write($"Variant\t");
                                    genotypingReport.Write($"{VCFrecord.CHROM}\t");
                                    genotypingReport.Write($"{VCFrecord.POS}\t");
                                    genotypingReport.Write($"{VCFrecord.REF}\t");
                                    genotypingReport.Write($"{VCFrecord.ALT}\t");
                                    genotypingReport.Write($"{ VCFrecord.QUAL}\t");
                                    genotypingReport.Write($"{VCFrecord.INFO["DP"]}\t");
                                    genotypingReport.Write($"{ADFields[0]}\t{ADFields[1]}\t");
                                    genotypingReport.Write($"{(Convert.ToDouble(VCFrecord.FORMAT["VF"]) * 100)}%\t");
                                    genotypingReport.WriteLine($"{VCFrecord.FORMAT["NL"]}\t{VCFrecord.FORMAT["SB"]}");
                                }
                            }
                        }
                    }

                    // Print Normal/Fail
                    foreach (BEDRecord region in BEDRecords)
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
                            // Skip mutant amplicons
                            continue;
                        }
                        else if (regiondepth < parameters.GenotypingDepth)
                        {
                            // This amplicon failed
                            genotypingReport.Write(sampleRecord.Sample_ID);
                            genotypingReport.Write("\t");
                            genotypingReport.Write(sampleRecord.Sample_Name);
                            genotypingReport.Write("\t" + region.Name);
                            genotypingReport.Write("\t" + parameters.GenotypingAnalysisVersion);
                            genotypingReport.Write("\tFailed\t\t\t\t\t\t");
                            genotypingReport.Write(regiondepth);
                            genotypingReport.WriteLine();
                        }
                        else
                        {
                            // Passing amplicon
                            genotypingReport.Write(sampleRecord.Sample_ID);
                            genotypingReport.Write("\t");
                            genotypingReport.Write(sampleRecord.Sample_Name);
                            genotypingReport.Write("\t" + region.Name);
                            genotypingReport.Write("\t" + parameters.GenotypingAnalysisVersion);
                            genotypingReport.Write("\tNo Mutation Detected\t\t\t\t\t\t");
                            genotypingReport.Write(regiondepth);
                            genotypingReport.WriteLine();
                        }

                    }
                    // Reset mutant amplicons
                    mutantAmplicons.Clear();
                }
            }
        }

        /// <summary>
        /// Generates a BED file from the AmpliconAligner input file
        /// </summary>
        private void GetGenotypingRegions()
        {
            bool passedFirstHeader = false;
            string line;
            string[] fields;

            using (StreamWriter GenotypingRegionsBED = new StreamWriter($@"{analysisDir}\GenotypingRegions.bed"))
            using (StreamReader ampliconAlignerV2Inputreader = new StreamReader(sampleSheet.Analyses["G"]))
            {
                // DEV: Could the line !- "" check go in here as a single line? Would remove an indent 
                while ((line = ampliconAlignerV2Inputreader.ReadLine()) != null)
                {
                    if (line != "")
                    {
                        if (line[0] == '#')
                        {

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
                        
                        // Check the input file has the expected number of fields
                        fields = line.Split('\t');
                        if (fields.Length != 7)
                        {
                            AuxillaryFunctions.WriteLog(@"AmpliconAligner input file is malformed. Check number of columns", parameters, errorCode: -1);
                            throw new FileLoadException();
                        }
                        // DEV: "checked" means that the wrapped code is protected against integer overflow
                        //      It's probably not necessary, as 32bit integers max out at 2,147,483,647 and there
                        //      are no human chromosomes that long. But maybe g. values could reach that high?
                        // OR we could use 64bit ints and remove the need for this wrapping.
                        checked
                        {
                            // 0-based start
                            int startPos = int.Parse(fields[2]) - 1;
                            // Field 3 is the sequence length, so we can compute the end position
                            int seqLen = fields[3].Length;
                            int endPos = startPos + seqLen;
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
                            BEDRecord tempRecord = new BEDRecord(chromosome: fields[1],
                                                                start: startPos,
                                                                end: endPos,
                                                                name: fields[0]);
                            BEDRecords.Add(tempRecord);
                            // DEV: This could be neatened up further by defining the way a BEDRecord prints
                            GenotypingRegionsBED.WriteLine($"{tempRecord.Chromosome}\t{tempRecord.Start}\t{tempRecord.End}\t{tempRecord.Name}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Read in the MappingStats file gnerated by the aligner for each sample
        /// </summary>
        private void AnalyseCoverageFromAlignerOutput()
        {
            AuxillaryFunctions.WriteLog(@"Calculating coverage values...", parameters);
            string line;

            // Loop over sampleIDs
            foreach (SampleRecord record in sampleSheet.SampleRecords)
            {
                if (record.Analysis != "G")
                {
                    continue;
                }
                // Read the MappingStats file
                using (FileStream stream = new FileStream($@"{analysisDir}\{record.Sample_ID}_MappingStats.txt", FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
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
                            ampliconMinDP.Add(new Tuple<string, string>(record.Sample_ID, fields[0]), int.Parse(fields[3]));
                        }
                    }
                }
            }
        }
    }
}