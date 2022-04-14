using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WinSCP;
using System.IO;
using System.Threading;
using System.Text.RegularExpressions;

namespace WRGLPipeline
{
    class PanelPipelineWrapper
    {
        // DEV: The panels pipeline version number is now set in WRGLPipeline.ini
        //      so we can update the scripts and version without rebuilding everything.

        // Tunnel connection settings
        private readonly string localAnalysisDir;
        private readonly string networkAnalysisDir;
        private readonly string localReportFilename;
        private readonly string networkReportFilename;
        private readonly string winscpLogPath;
        readonly ProgrammeParameters parameters;
        readonly ParseSampleSheet sampleSheet;
        readonly Dictionary<string, HashSet<string>> failedAmplicons = new Dictionary<string, HashSet<string>>();
        readonly ParseBED coreBEDRecords;
        readonly ParseBED targetBEDRecords;
        readonly Dictionary<string, Tuple<string, string>> fastqFileNames;

        /// <summary>
        /// Performs the panels pipeline analysis
        /// </summary>
        /// <param name="_sampleSheet">Parsed SampleSheet for the run</param>
        /// <param name="_parameters">Configure ProgrammeParameters</param>
        /// <param name="_fastqFileNames">Dictionary of sample IDs and corresponding R1 and R2 fastq files</param>
        /// <remarks>I'm not sure if this is really testable...</remarks>
        public PanelPipelineWrapper(ParseSampleSheet _sampleSheet, ProgrammeParameters _parameters,
            Dictionary<string, Tuple<string, string>> _fastqFileNames)
        {
            this.parameters = _parameters;
            this.sampleSheet = _sampleSheet;
            this.localAnalysisDir = $@"{parameters.LocalFastqDir}\Panel_{parameters.PanelsAnalysisVersion}";
            this.networkAnalysisDir = $@"{parameters.NetworkRootRunDir}\Panel_{parameters.PanelsAnalysisVersion}";
            this.localReportFilename = $@"{localAnalysisDir}\{parameters.RunID}_Panel_{parameters.PanelsAnalysisVersion}.report";
            this.networkReportFilename = $@"{networkAnalysisDir}\{parameters.RunID}_Panel_{parameters.PanelsAnalysisVersion}.report";
            this.winscpLogPath = $@"{localAnalysisDir}\{parameters.RunID}_WinSCP_Transfer.log";
            this.coreBEDRecords = new ParseBED(parameters.CoreBedFile, parameters);
            this.targetBEDRecords = new ParseBED(sampleSheet.Analyses[@"P"], parameters);
            this.fastqFileNames = _fastqFileNames;

            ExecutePanelPipeline();
        }

        /// <summary>
        /// Finalises configuration of the panels pipeline, and runs the required functions
        /// </summary>
        private void ExecutePanelPipeline()
        {
            AuxillaryFunctions.WriteLog(@"Starting panel pipeline...", parameters);

            // Create local output analysis directory
            try { Directory.CreateDirectory(localAnalysisDir); } catch (Exception e) {
                AuxillaryFunctions.WriteLog($@"Could not create local ouput directory: {e.ToString()}", parameters, errorCode: -1);
                throw;
            }

            // Create network output analysis directory
            if (parameters.CopyToNetwork)
            {
                try
                {
                    Directory.CreateDirectory(networkAnalysisDir);
                }
                catch (Exception e)
                {
                    AuxillaryFunctions.WriteLog($@"Could not create network ouput directory: {e.ToString()}", parameters, errorCode: -1);
                    throw;
                }
            }

            // Write variables files for all samples
            WriteVariablesFiles();

            // If getdata is false, run the UploadAndExecute function 
            if (!parameters.GetData) {

                // Upload and execute pipeline
                UploadAndExecute();

                // Wait before checking download
                AuxillaryFunctions.WriteLog(@"Pipeline idle. Going to sleep...", parameters);

                // TimeSpan is more intuitive than ticks/ms
                // sets (hours, minutes, seconds)
                TimeSpan waitTime = new TimeSpan(1, 0, 0);
                Thread.Sleep(waitTime);
            }

            //poll IRIDIS4 for run completion file
            for (int k = 0; k < 200; ++k)
            {
                AuxillaryFunctions.WriteLog(@"Download data attempt " + (k + 1), parameters);

                // Runs GetData and checks the result - false == pending, anything else == run complete and downloaded
                if (GetData() == false)
                {
                    AuxillaryFunctions.WriteLog(@"Pipeline idle. Going to sleep...", parameters);

                    // TimeSpan is more intuitive than ticks/ms
                    // sets (hours, minutes, seconds)
                    TimeSpan waitTime = new TimeSpan(0, 15, 0);
                    Thread.Sleep(waitTime); //ms wait 30 mins before checking again
                }
                else
                {
                    AuxillaryFunctions.WriteLog(@"Files downloaded sucessfully", parameters);

                    WritePanelReport();

                    //copy report file to network
                    if (parameters.CopyToNetwork)
                    {
                        File.Copy(localReportFilename, networkReportFilename, true);
                        // DEV: is this a duplicate of above??
                        File.Copy(localReportFilename, $@"{parameters.PanelRepo}\{parameters.RunID}_Panel_{parameters.PanelsAnalysisVersion}.report", true);
                    }

                    AuxillaryFunctions.WriteLog($@"Variant report path is {localReportFilename}", parameters);

                    // DEV: Remove all email functionality?
                    //AuxillaryFunctions.SendRunCompletionEmail(parameters.localLogFilename, parameters.getPanelRepo + @"\" + Path.GetFileName(localReportFilename), sampleSheet, @"Panel_" + parameters.PanelsAnalysisVersion, parameters.runID, parameters);

                    // Download BAM files unless otherwise specified by the user.
                    if ((parameters.BamDownload) && (parameters.CopyToNetwork))
                    {
                        DownloadBamsToLocalStore();
                    }
                    return;
                }
            }

            //data not downloaded
            AuxillaryFunctions.WriteLog(@"Data Colletion Timeout.", parameters, errorCode: -1);
            throw new TimeoutException();

        }

        /// <summary>
        /// Connects to Iridis, uploads data, and triggers remote analysis scripts 
        /// </summary>
        private void UploadAndExecute()
        {
            using (Session session = ConnectToIridis())
            {
                TransferOptions transferOptions = new TransferOptions
                {
                    TransferMode = TransferMode.Binary,
                    // set the permissions for uploaded files - -rwxrwx-- BS 2016-03-14.
                    FilePermissions = new FilePermissions { Text = "rwxrwx---" }
                };
                //StringBuilder bashCommand = new StringBuilder();
                string RemoteSampleFolder;

                //make remote project directory
                try
                {
                    AuxillaryFunctions.WriteLog($@"Creating remote directory {parameters.IridisWorkingDir}/{parameters.RunID}", parameters);
                    session.CreateDirectory($@"{parameters.IridisWorkingDir}/{parameters.RunID}");
                }
                catch (WinSCP.SessionRemoteException ex)
                {
                    AuxillaryFunctions.WriteLog($@"Could not create remote directory: {ex}", parameters, errorCode: -1);
                    throw;
                }

                // Upload preferred transcripts file
                session.PutFiles(parameters.PreferredTranscriptsPath, $@"{parameters.IridisWorkingDir}/{parameters.RunID}/", false, transferOptions).Check();

                //loop over Sample_IDs and upload FASTQs
                foreach (SampleRecord record in sampleSheet.SampleRecords)
                {
                    if (record.Analysis != @"P")
                    {
                        continue;
                    }

                    //output to user
                    AuxillaryFunctions.WriteLog($@"Uploading data for {record.Sample_ID}", parameters);

                    RemoteSampleFolder = $@"{parameters.IridisWorkingDir}/{parameters.RunID}/{record.Sample_ID}";

                    //make remote folder for Sample
                    session.CreateDirectory(RemoteSampleFolder);

                    //upload R1 FASTQ
                    session.PutFiles(fastqFileNames[record.Sample_ID].Item1, $@"{RemoteSampleFolder}/", false, transferOptions).Check();

                    //upload R2 FASTQ
                    session.PutFiles(fastqFileNames[record.Sample_ID].Item2, $@"{RemoteSampleFolder}/", false, transferOptions).Check();

                    //upload MD5 files
                    session.PutFiles(fastqFileNames[record.Sample_ID].Item1 + @".md5", $@"{RemoteSampleFolder}/", false, transferOptions).Check();

                    //upload MD5 files
                    session.PutFiles(fastqFileNames[record.Sample_ID].Item2 + @".md5", $@"{RemoteSampleFolder}/", false, transferOptions).Check();

                    //upload variables file
                    session.PutFiles(localAnalysisDir + @"\" + record.Sample_ID + @".variables", $@"{RemoteSampleFolder}/", false, transferOptions).Check();

                    //copy BEDfile to RemoteSamplefolder
                    session.PutFiles(sampleSheet.Analyses[@"P"], $@"{RemoteSampleFolder}/", false, transferOptions).Check();
                }

                // Change to the run directory
                session.ExecuteCommand($@"cd {parameters.IridisWorkingDir}/{parameters.RunID}");
                // Update file permissions
                session.ExecuteCommand($@"chmod -R 770 . && chgrp -R wrgl .");
                // Start the pipeline runner script - log output in file to catch errors
                session.ExecuteCommand($@"{parameters.PanelScriptsDir}/pipeline_runner.sh > bashCommand.log 2>&1");
            }
        }

        /// <summary>
        /// Connects to Iridis, checks if run is complete, and downloads data if so
        /// </summary>
        /// <returns></returns>
        /// <remarks>Iridis connection really needs to be refactored - it's duplicated in several functions</remarks>
        private bool GetData()
        {
            using (Session session = ConnectToIridis())
            {
                //check if job is complete
                if (session.FileExists($@"{parameters.IridisWorkingDir}/{parameters.RunID}/complete"))
                {
                    AuxillaryFunctions.WriteLog(@"Analysis complete. Retrieving data...", parameters);

                    // Download files and throw on any error
                    // DEV: Does localAnalysisDir need the trailing backslash? I'd think the function recognisese it's copying a file to a folder...
                    TransferOptions transferoptions = new TransferOptions() { OverwriteMode = OverwriteMode.Overwrite, TransferMode=TransferMode.Binary };
                    session.GetFiles($@"{parameters.IridisWorkingDir}/{parameters.RunID}/{parameters.RunID}_Filtered_Annotated.vcf", $@"{localAnalysisDir}\", false, transferoptions).Check();
                    session.GetFiles($@"{parameters.IridisWorkingDir}/{parameters.RunID}/BAMsforDepthAnalysis.list", localAnalysisDir + @"\", false, transferoptions);
                    session.GetFiles($@"{parameters.IridisWorkingDir}/{parameters.RunID}/{parameters.RunID}_Coverage.txt", localAnalysisDir + @"\", false, transferoptions);
                    session.GetFiles($@"{parameters.IridisWorkingDir}/{parameters.RunID}/{parameters.PreferredTranscriptsFile}", localAnalysisDir + @"\", false, transferoptions);
                    session.GetFiles($@"{parameters.IridisWorkingDir}/{parameters.RunID}/*.bed", localAnalysisDir + @"\", false, transferoptions);
                    session.GetFiles($@"{parameters.IridisWorkingDir}/{parameters.RunID}/*.sh", localAnalysisDir + @"\", false, transferoptions);
                    session.GetFiles($@"{parameters.IridisWorkingDir}/{parameters.RunID}/*.config", localAnalysisDir + @"\", false, transferoptions);
                    session.GetFiles($@"{parameters.IridisWorkingDir}/{parameters.RunID}/*.slurm", localAnalysisDir + @"\", false, transferoptions);
                    session.GetFiles($@"{parameters.IridisWorkingDir}/{parameters.RunID}/*.r", localAnalysisDir + @"\", false, transferoptions);
                    session.GetFiles($@"{parameters.IridisWorkingDir}/{parameters.RunID}/{parameters.RunID}_CNVs.report", localAnalysisDir + @"\", false, transferoptions);

                    // If <parameters.runID>_genecoverage.zip file exists download it
                    try
                    {                        
                        session.GetFiles($@"{parameters.IridisWorkingDir}/{parameters.RunID}/{parameters.RunID}_genecoverage.zip", localAnalysisDir + @"\").Check();
                        // And move it to the network
                        if (parameters.CopyToNetwork)
                        {
                            File.Copy($@"{localAnalysisDir}\{parameters.RunID}_genecoverage.zip", $@"{networkAnalysisDir}\{parameters.RunID}_genecoverage.zip", true);
                        }
                    }
                    catch
                    {
                        // If it doesn't exist, catch the error and log as this file is not essential (for this step of the process)
                        AuxillaryFunctions.WriteLog(@"No genecoverage.zip file found", parameters, errorCode: 1);
                    }

                    // Download a single copy of the scripts from the first sample
                    // This is needed as we don't have a copy of all scripts in the root run folder.
                    session.GetFiles($@"{parameters.IridisWorkingDir}/{parameters.RunID}/{sampleSheet.SampleRecords[1].Sample_ID}/*.sh", localAnalysisDir + @"\").Check();

                    // Copy to network
                    if (parameters.CopyToNetwork)
                    {
                        File.Copy($@"{localAnalysisDir}\{parameters.RunID}_Filtered_Annotated.vcf", $@"{networkAnalysisDir}\{parameters.RunID}_Filtered_Annotated.vcf", true);
                        File.Copy($@"{localAnalysisDir}\BAMsforDepthAnalysis.list", $@"{networkAnalysisDir}\BAMsforDepthAnalysis.list", true);
                        File.Copy($@"{localAnalysisDir}\{parameters.RunID}_Coverage.txt", $@"{networkAnalysisDir}\{parameters.RunID}_Coverage.txt", true);
                        File.Copy($@"{localAnalysisDir}\{parameters.PreferredTranscriptsFile}", $@"{networkAnalysisDir}\{parameters.PreferredTranscriptsFile}", true);
                        // Copy the CNV report to both the Z: run folder *and* to the CNV reports repository folder
                        File.Copy($@"{localAnalysisDir}\{parameters.RunID}_CNVs.report", $@"{networkAnalysisDir}\{parameters.RunID}_CNVs.report", true);
                        File.Copy($@"{localAnalysisDir}\{parameters.RunID}_CNVs.report", $@"{parameters.CNVRepo}\{parameters.RunID}_CNVs.report", true);
                        // Copy multiple files to the network - File.Copy only does single files, and also can't handle wildcards
                        foreach (var f in Directory.GetFiles(localAnalysisDir).Where(path => Regex.Match(path, @".*.bed").Success)) { File.Copy(f, $@"{networkAnalysisDir}\{Path.GetFileName(f)}", true); }
                        foreach (var f in Directory.GetFiles(localAnalysisDir).Where(path => Regex.Match(path, @".*.sh").Success)) { File.Copy(f, $@"{networkAnalysisDir}\{Path.GetFileName(f)}", true); }
                        foreach (var f in Directory.GetFiles(localAnalysisDir).Where(path => Regex.Match(path, @".*.slurm").Success)) { File.Copy(f, $@"{networkAnalysisDir}\{Path.GetFileName(f)}", true); }
                        foreach (var f in Directory.GetFiles(localAnalysisDir).Where(path => Regex.Match(path, @".*.r").Success)) { File.Copy(f, $@"{networkAnalysisDir}\{Path.GetFileName(f)}", true); }
                    }

                    return true;
                }
                // If the run doesn't appear to have finished, display a message so the user knows this.
                else
                {
                    AuxillaryFunctions.WriteLog(@"Marker File Not Found. Run is Pending", parameters);
                    return false;
                }
            }
        }

        /// <summary>
        /// Write the local report file, summarising the variants detected in every sample.
        /// </summary>
        private void WritePanelReport()
        {
            AuxillaryFunctions.WriteLog(@"Writing panel report...", parameters);

            // Read the run VCF file and the BCInt annotation files
            ParseVCF VCFFile = new ParseVCF($@"{localAnalysisDir}\{parameters.RunID}_Filtered_Annotated.vcf", parameters);
            ParseVCF BCIntFile = new ParseVCF(parameters.InterpretationsFile, parameters);
            Dictionary<GenomicVariant, string> interpretations = new Dictionary<GenomicVariant, string>();

            using (StreamWriter panelReport = new StreamWriter(localReportFilename))
            {
                panelReport.WriteLine("SampleID\tSampleName\tGene\tHGVSc\tHGVSp\tExon\tGenotype\tTranscriptID\tFunction\tInterpretation\tHTSFAmplicon\tChromosome\tPosition\tReference\tAlternative");

                // Get failed amplicons
                AnalyseCoverageData($@"{localAnalysisDir}\{parameters.RunID}_Coverage.txt", $@"{localAnalysisDir}\BAMsforDepthAnalysis.list");

                // Load BC ints
                AuxillaryFunctions.WriteLog(@"Loading BCInterpretations file...", parameters);
                foreach (VCFRecordWithGenotype record in BCIntFile.VCFRecords[""]) //loop over interpretations
                {
                    GenomicVariant tempGenomicVariant = new GenomicVariant(CHROM: record.CHROM, POS: record.POS, REF: record.REF, ALT: record.ALT);
                    // Store the annotated variant info - with fallback option to handle the different SNPEff output formats
                    // as both have been used over the years and we might need to deal with both.
                    try
                    {
                        interpretations.Add(tempGenomicVariant, record.INFO["INT"]);
                    }
                    catch
                    {
                        AuxillaryFunctions.WriteLog(@"Could not find INT parameter...", parameters, errorCode: 1);
                        try
                        {
                            interpretations.Add(tempGenomicVariant, record.INFO["EFF"]);
                        }
                        catch
                        {
                            AuxillaryFunctions.WriteLog($@"Could not find EFF parameter in {record.ToString()}", parameters, errorCode: 1);
                            throw;
                        }
                    }
                }

                // Loop over samples for panel analysis
                foreach (SampleRecord record in sampleSheet.SampleRecords)
                {
                    // Skip if the sample isn't for panels analysis
                    if (record.Analysis != @"P")
                    {
                        continue;
                    }

                    if (!VCFFile.VCFRecords.ContainsKey(record.Sample_ID))
                    {
                        // Throw an error if there are no genotypes - we expect *something*, even if the
                        // variant is not present in this sample. No genotype indicates an unexpected error.
                        AuxillaryFunctions.WriteLog($@"Sample {record.Sample_ID} has no genotypes in panel VCF", parameters, errorCode: -1);
                        throw new FileLoadException();
                    }

                    // Loop over VCF variant records for this sample
                    foreach (VCFRecordWithGenotype VCFrecord in VCFFile.VCFRecords[record.Sample_ID])
                    {
                        GenomicVariant tempGenomicVariant = new GenomicVariant(CHROM: VCFrecord.CHROM, POS: VCFrecord.POS, REF: VCFrecord.REF, ALT: VCFrecord.ALT);

                        // Skip hom ref variants - not needed in final report
                        if (VCFrecord.FORMAT["GT"] == @"0/0")
                        {
                            continue;
                        }

                        // Check if an annotation is available
                        if (VCFFile.SnpEffAnnotations.ContainsKey(tempGenomicVariant))
                        {
                            // Loop over annotations - in case there are more than one (e.g. multiple transcripts)
                            foreach (Annotation ann in VCFFile.SnpEffAnnotations[tempGenomicVariant])
                            {
                                panelReport.Write(record.Sample_ID + "\t");
                                panelReport.Write(record.Sample_Name + "\t");

                                // -- Annotation specific section
                                panelReport.Write(ann.Gene_Name + "\t");

                                // DEV: Potentiall this could be refactored out to a function?
                                string[] hgvs = ann.Amino_Acid_Change.Split('/');
                                // Check if full HGVS (i.e. c. and p.) are available
                                if (hgvs.Length == 2)
                                {
                                    panelReport.Write(hgvs[1] + "\t"); //c.
                                    panelReport.Write(hgvs[0] + "\t"); //p.
                                }
                                else if (hgvs.Length == 1)
                                {
                                    panelReport.Write(hgvs[0] + "\t"); //c. or n.
                                    panelReport.Write("\t");
                                }
                                else
                                {
                                    panelReport.Write("\t\t");
                                }

                                // Print exon
                                panelReport.Write("{0}\t", ann.Exon_Rank);

                                // Use the refactored function instead of the above
                                // This *could* be found before and saved to a variable, to avoid calling it in two places
                                panelReport.Write($"{ParseGenotype(VCFrecord)}\t");
                                panelReport.Write(ann.Transcript_ID + "\t");
                                panelReport.Write(ann.Effect + "\t");

                                // -- END annotation specific section

                                if (interpretations.ContainsKey(tempGenomicVariant))
                                {
                                    panelReport.Write(interpretations[tempGenomicVariant] + "\t");
                                }
                                else
                                {
                                    panelReport.Write("\t");
                                }

                                // Print HTSF amplicon (if present)
                                panelReport.Write(AuxillaryFunctions.LookupAmpliconID(new Tuple<string, int>(VCFrecord.CHROM, VCFrecord.POS), coreBEDRecords.BEDRecords) + "\t");
                                panelReport.Write(VCFrecord.CHROM + "\t");
                                panelReport.Write(VCFrecord.POS + "\t");
                                panelReport.Write(VCFrecord.REF + "\t");
                                panelReport.Write(VCFrecord.ALT + "\t");
                                panelReport.WriteLine();
                            }

                        }
                        else // No annotation
                        {
                            panelReport.Write(record.Sample_ID + "\t");
                            panelReport.Write(record.Sample_Name + "\t");

                            // Empty columns for the annotation-specific details
                            panelReport.Write("\t\t\t\t");

                            // DEV: Try using the function instead of all the above. Also include the blank columns
                            //      where there is no SNPEff annotation (for transcript and predicted effect)
                            panelReport.Write($"{ParseGenotype(VCFrecord)}\t\t\t");

                            if (interpretations.ContainsKey(tempGenomicVariant))
                            {
                                panelReport.Write(interpretations[tempGenomicVariant] + "\t");
                            }
                            else
                            {
                                panelReport.Write("\t");
                            }

                            //print HTSF amplicon (if present)
                            panelReport.Write(AuxillaryFunctions.LookupAmpliconID(new Tuple<string, int>(VCFrecord.CHROM, VCFrecord.POS), coreBEDRecords.BEDRecords) + "\t");
                            panelReport.Write(VCFrecord.CHROM + "\t");
                            panelReport.Write(VCFrecord.POS + "\t");
                            panelReport.Write(VCFrecord.REF + "\t");
                            panelReport.Write(VCFrecord.ALT + "\t");
                            panelReport.WriteLine();
                        }
                    }

                    // Add gaps
                    if (!failedAmplicons.ContainsKey(record.Sample_ID))
                    {
                        AuxillaryFunctions.WriteLog($@"Sample {record.Sample_ID} coverage data not loaded successfully!", parameters, errorCode: -1);
                        throw new FileLoadException();
                    }

                    // Print failed regions
                    foreach (string failedCoreRegion in failedAmplicons[record.Sample_ID])
                    {
                        panelReport.WriteLine($"{record.Sample_ID}\t{record.Sample_Name}\t\t\t\t\t\t\t\tGAP\t{failedCoreRegion}");
                    }
                }
            }
        }

        /// <summary>
        /// Interprets the genotype for the variant, and returns a textual representation e.g. HET, HOM.
        /// Now updated to account for phased variants (| instead of /)
        /// </summary>
        /// <param name="VCFrecord">The variant record object to be interpreted</param>
        /// <returns>string interpretation of the variant genotype</returns>
        private string ParseGenotype(VCFRecordWithGenotype VCFrecord)
        {
            // 1/0 is not technically VCF spec, but is output by some tools so we should account for it
            if (VCFrecord.FORMAT["GT"] == @"0/1" || VCFrecord.FORMAT["GT"] == @"1/0" || VCFrecord.FORMAT["GT"] == @"0|1" || VCFrecord.FORMAT["GT"] == @"1|0")
            {
                return "HET";
            }
            else if (VCFrecord.FORMAT["GT"] == @"1/1" || VCFrecord.FORMAT["GT"] == @"1|1")
            {
                return "HOM_ALT";
            }
            // Uncertain genotypes (i.e. with ".") should be flagged.
            // in all likelihood they will be flagged as GAPs, but best to be sure
            else if (VCFrecord.FORMAT["GT"] == @"./1" || VCFrecord.FORMAT["GT"] == @"1/." || VCFrecord.FORMAT["GT"] == @".|1" || VCFrecord.FORMAT["GT"] == @"1|.")
            {
                return "UNCERTAIN_HET";
            }
            // Homozygous uncertain genotypes are almost certainly gaps, and are filtered by the current pipeline
            // but for futureproofing/compatibility with other pipelines they are included
            else if (VCFrecord.FORMAT["GT"] == @"./." || VCFrecord.FORMAT["GT"] == @".|.")
            {
                return "UNCERTAIN_HOM";
            }
            else if (VCFrecord.FORMAT["GT"] == @"")
            {
                return "Unknown";
            }
            else
            {
                // If the genotype is something not accounted for, flag it as potentially needing
                // further checking (e.g. multiple alleles, possibly across several samples).
                return "Complex";
            }
        }

        /// <summary>
        /// Work out which amplicons contain gaps below the minimum required coverage 
        /// </summary>
        /// <param name="samtoolsDepthFilePath">_Coverage file for the run</param>
        /// <param name="samtoolsDepthSampleIDFilePath">CoverageSampleOrder.txt file</param>
        private void AnalyseCoverageData(string samtoolsDepthFilePath, string samtoolsDepthSampleIDFilePath)
        {
            AuxillaryFunctions.WriteLog(@"Analysing coverage data...", parameters);

            string line, failedAmpliconID;
            int pos;
            Dictionary<Tuple<string, int>, bool> isBaseCovered = new Dictionary<Tuple<string, int>, bool>(); //bool = observed
            List<string> sampleIDs = new List<string>();

            //read sampleID order
            using (FileStream stream = new FileStream(samtoolsDepthSampleIDFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader fileInput = new StreamReader(stream))
            {
                {
                    while ((line = fileInput.ReadLine()) != null)
                    {
                        string[] fields = line.Split('_', '.');
                        sampleIDs.Add(fields[fields.Length - 2]);
                        failedAmplicons.Add(fields[fields.Length - 2], new HashSet<string>());
                    }
                }
            }

            //loop over target ROI, hash bases
            foreach (BEDRecord record in targetBEDRecords.BEDRecords)
            {
                //iterate over region
                for (pos = record.Start + 2; pos < record.End + 1; ++pos)
                {
                    if (!isBaseCovered.ContainsKey(new Tuple<string, int>(record.Chromosome, pos)))
                    {
                        isBaseCovered.Add(new Tuple<string, int>(record.Chromosome, pos), false);
                    }
                }

            }

            // loop over output and assign failed to low coverage amplicons
            using (FileStream stream = new FileStream(samtoolsDepthFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader reader = new StreamReader(stream))
            {
                {
                    // Loop over the lines in the string.
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] fields = line.Split('\t');

                        pos = int.Parse(fields[1]);

                        //mark base as observed in the dataset
                        isBaseCovered[new Tuple<string, int>(fields[0], pos)] = true;

                        for (int n = 2; n < fields.Length; ++n) //skip chrom & pos
                        {
                            if (int.Parse(fields[n]) < parameters.PanelsDepth) //base has failed
                            {
                                //mark amplicon as failed
                                failedAmpliconID = AuxillaryFunctions.LookupAmpliconID(new Tuple<string, int>(fields[0], pos), coreBEDRecords.BEDRecords);

                                if (failedAmpliconID != "") //skip off target
                                {
                                    failedAmplicons[sampleIDs[n - 2]].Add(failedAmpliconID);
                                }
                            }
                        }
                    }
                }
            }

            //report missing bases as failed
            foreach (KeyValuePair<Tuple<string, int>, bool> nucl in isBaseCovered)
            {
                if (nucl.Value == false) //base not present in dataset
                {
                    failedAmpliconID = AuxillaryFunctions.LookupAmpliconID(nucl.Key, coreBEDRecords.BEDRecords);

                    if (failedAmpliconID != "") //skip off target
                    {
                        foreach (string sampleID in sampleIDs)
                        {
                            //mark amplicon as failed
                            failedAmplicons[sampleID].Add(failedAmpliconID);
                        }
                    }

                }
            }
            //DEV
            AuxillaryFunctions.WriteLog(@"Analysing coverage data complete.", parameters);
        }

        /// <summary>
        /// Writes variables file for each sample. Variables file contains details requried for Linux analysis scripts.
        /// </summary>
        private void WriteVariablesFiles()
        {
            AuxillaryFunctions.WriteLog(@"Writing variable files...", parameters);

            //concatinate analysisdirs
            StringBuilder AnalysisDirs = new StringBuilder(@"AnalysisDirs=( ");

            foreach (SampleRecord record in sampleSheet.SampleRecords)
            {
                if (record.Analysis == @"P") {
                    AnalysisDirs.Append($"\"{parameters.IridisWorkingDir}/{parameters.RunID}/{record.Sample_ID}\" ");
                }
            }
            AnalysisDirs.Append(')');

            foreach (SampleRecord record in sampleSheet.SampleRecords)
            {
                if (record.Analysis != @"P") {
                    continue;
                }

                //open variables output file
                StreamWriter VarFile = new StreamWriter(localAnalysisDir + @"\" + record.Sample_ID + @".variables")
                {
                    NewLine = "\n"
                };

                VarFile.WriteLine("#Description: Pipeline Variables File");

                VarFile.WriteLine("\n#Sample_ID");
                VarFile.WriteLine($@"Sample_ID={record.Sample_ID}");

                VarFile.WriteLine("\n#FASTQ MD5 checksum");
                VarFile.WriteLine($@"R1MD5Filename={Path.GetFileName(fastqFileNames[record.Sample_ID].Item1)}.md5");
                VarFile.WriteLine($@"R2MD5Filename={Path.GetFileName(fastqFileNames[record.Sample_ID].Item2)}.md5");

                VarFile.WriteLine("\n#FASTQ filenames");
                VarFile.WriteLine($@"R1Filename={Path.GetFileName(fastqFileNames[record.Sample_ID].Item1)}");
                VarFile.WriteLine($@"R2Filename={Path.GetFileName(fastqFileNames[record.Sample_ID].Item2)}");

                VarFile.WriteLine("\n#Capture ROI");
                VarFile.WriteLine($@"BEDFilename={Path.GetFileName(sampleSheet.Analyses[@"P"])}");

                VarFile.WriteLine("\n#RunDetails");
                VarFile.WriteLine($@"RunID={parameters.RunID}");
                VarFile.WriteLine($@"ExperimentName={sampleSheet.ExperimentName}");
                VarFile.WriteLine("Platform=ILLUMINA");

                VarFile.WriteLine("\n#Annotation");
                VarFile.WriteLine($@"PreferredTranscriptsFile={parameters.IridisWorkingDir}/{parameters.RunID}/{Path.GetFileName(parameters.PreferredTranscriptsFile)}");

                VarFile.WriteLine("\n#AnalysisFolders");
                VarFile.WriteLine($@"{AnalysisDirs}");

                VarFile.Close();
            }
        }

        /// <summary>
        /// To run after report generation
        /// Download all run BAM files from Iridis and put them in the local temporary
        /// store for access with IGV.
        /// </summary>
        private void DownloadBamsToLocalStore()
        {
            AuxillaryFunctions.WriteLog(@"Starting BAM downloader...", parameters);

            // Create the run folder in the Panels BAM store
            string RunBamStore = $@"{parameters.BamStoreLocation}\Panels\{parameters.RunID}";

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

            // Connect to Iridis and download the BAM files for each sample
            using (Session session = ConnectToIridis())
            {
                //TODO
                //TransferOptions transferOptions = new TransferOptions();
                //transferOptions.TransferMode = TransferMode.Binary;
                TransferOptions transferOptions = new TransferOptions
                {
                    TransferMode = TransferMode.Binary,
                    OverwriteMode = OverwriteMode.Resume,
                };
                // I don't know why this can't be defined with the rest of the transferOptions above!
                transferOptions.ResumeSupport.State = TransferResumeSupportState.On;

                // For each sample, download the BAM file
                foreach (SampleRecord record in sampleSheet.SampleRecords)
                {
                    string BamRootFileRemote = $@"{parameters.IridisWorkingDir}/{parameters.RunID}/{record.Sample_ID}/{parameters.RunID}_{record.Sample_ID}";
                    string BamRootFileLocal = $@"{RunBamStore}\{parameters.RunID}_{record.Sample_ID}";

                    AuxillaryFunctions.WriteLog($@"Downloading {BamRootFileRemote} to {BamRootFileLocal}...", parameters);

                    if ( ! File.Exists($@"{BamRootFileLocal}.bam"))
                    {
                        session.GetFiles($@"{BamRootFileRemote}.bam", $@"{BamRootFileLocal}.bam", false, transferOptions).Check();
                        // DEV: Iridis 5 pipeline now output .bam.bai, not just .bai file extension.
                        session.GetFiles($@"{BamRootFileRemote}.bam.bai", $@"{BamRootFileLocal}.bam.bai", false, transferOptions).Check();
                    }   
                }
            }
        }

        /// <summary>
        /// Creates a Session object connected to iridis, so this functionality doens't need to be repeated.
        /// </summary>
        /// <returns></returns>
        private Session ConnectToIridis()
        {
            // DEV: Re-add the loop through A, B, and C nodes as Iridis 5 retains this structure.
            //      Will need to add the server fingerprints for each node into the .ini file.

            // Create the session object and set up the WinSCP logging file
            SessionOptions iridisSessionOptions = new SessionOptions
            {
                Protocol = Protocol.Sftp,
                UserName = parameters.SotonUserName,
                Password = ProgrammeParameters.ToInsecureString(parameters.SotonPassword),
            };
            iridisSessionOptions.AddRawSettings(@"Tunnel", @"1");
            iridisSessionOptions.AddRawSettings(@"TunnelHostName", parameters.SSHHostName);
            iridisSessionOptions.AddRawSettings(@"TunnelUserName", parameters.SotonUserName);
            iridisSessionOptions.AddRawSettings(@"TunnelPasswordPlain", ProgrammeParameters.ToInsecureString(parameters.SotonPassword));
            iridisSessionOptions.AddRawSettings(@"TunnelHostKey", parameters.SSHHostKey);

            Session session = new Session
            {
                SessionLogPath = winscpLogPath
            };

            // Try to connect to each Iridis login node in turn, in case one of them is down
            try
            {
                // Connect to Iridis5a
                iridisSessionOptions.HostName = parameters.Iridis5aHostName;
                iridisSessionOptions.SshHostKeyFingerprint = parameters.Iridis5aHostKey;
                AuxillaryFunctions.WriteLog($@"Connecting To {iridisSessionOptions.HostName}...", parameters);
                session.Open(iridisSessionOptions);
            }
            catch (WinSCP.SessionRemoteException a)
            {
                AuxillaryFunctions.WriteLog($@"Could not connect: {a.ToString()}", parameters, errorCode: 1);
                // DEV: instead of throwing, try/catch the remaining login nodes
                try
                {
                     // Connect to Iridis5b
                     iridisSessionOptions.HostName = parameters.Iridis5bHostName;
                     iridisSessionOptions.SshHostKeyFingerprint = parameters.Iridis5bHostKey;
                     AuxillaryFunctions.WriteLog($@"Connecting To {iridisSessionOptions.HostName}...", parameters);
                     session.Open(iridisSessionOptions);
                }
                catch (WinSCP.SessionRemoteException b)
                {
                    AuxillaryFunctions.WriteLog($@"Could not connect: {b.ToString()}", parameters, errorCode: 1);
                    try
                    {
                        // Connect to Iridis5c
                        iridisSessionOptions.HostName = parameters.Iridis5cHostName;
                        iridisSessionOptions.SshHostKeyFingerprint = parameters.Iridis5cHostKey;
                        AuxillaryFunctions.WriteLog($@"Connecting To {iridisSessionOptions.HostName}...", parameters);
                        session.Open(iridisSessionOptions);
                    }
                    catch (WinSCP.SessionRemoteException c)
                    {
                        AuxillaryFunctions.WriteLog($@"Could not connect: {c.ToString()}", parameters, errorCode: 1);
                        AuxillaryFunctions.WriteLog(@"Could not connect: to any Iridis 5 login node", parameters, errorCode: -1);
                        throw;
                    }
                }
            }
            // If we reach here then we should have a correctly opened session!
            return session;
        }
    }
}
