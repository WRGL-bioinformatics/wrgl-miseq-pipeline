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
        const double PanelPipelineVerison = 2.21;

        //tunnel connection settings
        private readonly string scratchDir;
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
            this.localAnalysisDir = parameters.LocalFastqDir + @"\Panel_" + PanelPipelineVerison;
            this.networkAnalysisDir = parameters.NetworkRootRunDir + @"\Panel_" + PanelPipelineVerison;
            this.localReportFilename = localAnalysisDir + @"\" + parameters.RunID + @"_Panel_" + PanelPipelineVerison + ".report";
            this.networkReportFilename = networkAnalysisDir + @"\" + parameters.RunID + @"_Panel_" + PanelPipelineVerison + ".report";
            this.winscpLogPath = localAnalysisDir + @"\" + parameters.RunID + @"_WinSCP_Transfer.log";
            this.scratchDir = @"/scratch/WRGL/";
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
            AuxillaryFunctions.WriteLog(@"Starting panel pipeline...", parameters.LocalLogFilename, 0, false, parameters);

            //create local output analysis directory
            try { Directory.CreateDirectory(localAnalysisDir); } catch (Exception e) {
                AuxillaryFunctions.WriteLog(@"Could not create local ouput directory: " + e.ToString(), parameters.LocalLogFilename, -1, false, parameters);
                throw;
            }

            //create network output analysis directory
            if (parameters.CopyToNetwork)
            {
                try
                {
                    Directory.CreateDirectory(networkAnalysisDir);
                }
                catch (Exception e)
                {
                    AuxillaryFunctions.WriteLog(@"Could not create network ouput directory: " + e.ToString(), parameters.LocalLogFilename, -1, false, parameters);
                    throw;
                }
            }

            // Write variables files for all samples
            WriteVariablesFiles();

            // if getdata is false, run the UploadAndExecute function 
            if (!parameters.GetData) {

                //upload and execute pipeline
                UploadAndExecute();

                //wait before checking download
                AuxillaryFunctions.WriteLog(@"Pipeline idle. Going to sleep...", parameters.LocalLogFilename, 0, false, parameters);

                // TimeSpan is more intuitive than ticks/ms
                // sets (hours, minutes, seconds)
                TimeSpan waitTime = new TimeSpan(1, 0, 0);
                Thread.Sleep(waitTime);
            }

            //poll IRIDIS4 for run completion file
            for (int k = 0; k < 200; ++k)
            {
                AuxillaryFunctions.WriteLog(@"Download data attempt " + (k + 1), parameters.LocalLogFilename, 0, false, parameters);

                // Runs GetData and checks the result - false == pending, anything else == run complete and downloaded
                if (GetData() == false)
                {
                    AuxillaryFunctions.WriteLog(@"Pipeline idle. Going to sleep...", parameters.LocalLogFilename, 0, false, parameters);

                    // TimeSpan is more intuitive than ticks/ms
                    // sets (hours, minutes, seconds)
                    TimeSpan waitTime = new TimeSpan(0, 15, 0);
                    Thread.Sleep(waitTime); //ms wait 30 mins before checking again
                }
                else
                {
                    AuxillaryFunctions.WriteLog(@"Files downloaded sucessfully", parameters.LocalLogFilename, 0, false, parameters);

                    WritePanelReport();

                    //copy file to network
                    if (parameters.CopyToNetwork)
                    {
                        File.Copy(localReportFilename, networkReportFilename); //report
                        File.Copy(localReportFilename, parameters.PanelRepo + @"\" + parameters.RunID + @"_Panel_" + PanelPipelineVerison + ".report");
                    }

                    AuxillaryFunctions.WriteLog(@"Variant report path is " + localReportFilename, parameters.LocalLogFilename, 0, false, parameters);

                    // DEV: Remove all email functionality?
                    //AuxillaryFunctions.SendRunCompletionEmail(parameters.localLogFilename, parameters.getPanelRepo + @"\" + Path.GetFileName(localReportFilename), sampleSheet, @"Panel_" + PanelPipelineVerison, parameters.runID, parameters);

                    // Download BAM files unless otherwise specified by the user.
                    if ( parameters.BamDownload)
                    {
                        DownloadBamsToLocalStore();
                    }
                    return;
                }
            }

            //data not downloaded
            AuxillaryFunctions.WriteLog(@"Data Colletion Timeout.", parameters.LocalLogFilename, -1, false, parameters);
            throw new TimeoutException();

        }

        /// <summary>
        /// Connects to Iridis, uploads data, and triggers remote analysis scripts 
        /// </summary>
        private void UploadAndExecute()
        {
            using (Session session = ConnectToIridis())
            {
                TransferOperationResult transferResult;
                TransferOptions transferOptions = new TransferOptions
                {
                    TransferMode = TransferMode.Binary,
                    // set the permissions for uploaded files - -rwxrwx-- BS 2016-03-14.
                    FilePermissions = new FilePermissions { Text = "rwxrwx---" }
                };
                StringBuilder bashCommand = new StringBuilder();
                string RemoteSampleFolder;

                //make remote project directory
                try
                {
                    AuxillaryFunctions.WriteLog(@"Creating remote directory " + scratchDir + parameters.RunID, parameters.LocalLogFilename, 0, false, parameters);
                    session.CreateDirectory(scratchDir + parameters.RunID);
                }
                catch (WinSCP.SessionRemoteException ex)
                {
                    AuxillaryFunctions.WriteLog(@"Could not create remote directory: " + ex.ToString(), parameters.LocalLogFilename, -1, false, parameters);
                    throw;
                }

                // Upload preferred transcripts file
                // This can be done with the full file path from the .ini file, but to download we will need just the file name
                // DEV: Can this be one-lined?
                //transferResult = session.PutFiles(parameters.PreferredTranscriptsPath, scratchDir + parameters.RunID + @"/", false, transferOptions);
                //transferResult.Check(); // Throw on any error

                // DEV: If this works, update the transfers below to match
                session.PutFiles(parameters.PreferredTranscriptsPath, scratchDir + parameters.RunID + @"/", false, transferOptions).Check();

                //loop over Sample_IDs and upload FASTQs
                foreach (SampleRecord record in sampleSheet.SampleRecords)
                {
                    if (record.Analysis != @"P")
                    {
                        continue;
                    }

                    //output to user
                    AuxillaryFunctions.WriteLog(@"Uploading data for " + record.Sample_ID, parameters.LocalLogFilename, 0, false, parameters);

                    RemoteSampleFolder = scratchDir + parameters.RunID + @"/" + record.Sample_ID;

                    //make remote folder for Sample
                    session.CreateDirectory(RemoteSampleFolder);

                    //upload R1 FASTQ
                    transferResult = session.PutFiles(fastqFileNames[record.Sample_ID].Item1, RemoteSampleFolder + @"/", false, transferOptions);
                    transferResult.Check(); // Throw on any error

                    //upload R2 FASTQ
                    transferResult = session.PutFiles(fastqFileNames[record.Sample_ID].Item2, RemoteSampleFolder + @"/", false, transferOptions);
                    transferResult.Check(); // Throw on any error

                    //upload MD5 files
                    transferResult = session.PutFiles(fastqFileNames[record.Sample_ID].Item1 + @".md5", RemoteSampleFolder + @"/", false, transferOptions);
                    transferResult.Check(); // Throw on any error

                    //upload MD5 files
                    transferResult = session.PutFiles(fastqFileNames[record.Sample_ID].Item2 + @".md5", RemoteSampleFolder + @"/", false, transferOptions);
                    transferResult.Check(); // Throw on any error

                    //upload variables file
                    transferResult = session.PutFiles(localAnalysisDir + @"\" + record.Sample_ID + @".variables", RemoteSampleFolder + @"/", false, transferOptions);
                    transferResult.Check(); // Throw on any error

                    //copy BEDfile to RemoteSamplefolder
                    transferResult = session.PutFiles(sampleSheet.Analyses[@"P"], RemoteSampleFolder + @"/", false, transferOptions);
                    transferResult.Check(); // Throw on any error

                    //build BASH command
                    bashCommand.Append(@"cd ");
                    bashCommand.Append(RemoteSampleFolder);
                    bashCommand.Append(@" ");
                    bashCommand.Append(@"&& ");
                    // set permissions for the run folder and all subfolders - BS 2016-03-14
                    bashCommand.Append(@"chmod -R 770 ..");
                    bashCommand.Append(@" ");
                    bashCommand.Append(@"&& ");
                    // set group to wrgl for everything in the run folder
                    // probably doesn't matter, but it's probably better to than not
                    bashCommand.Append(@"chgrp -R wrgl ..");
                    bashCommand.Append(@" ");
                    bashCommand.Append(@"&& ");
                    bashCommand.Append(parameters.PanelScriptsDir + @"/aux_scripts/aux0_Start_Pipeline.sh");
                    //execute pipeline
                    session.ExecuteCommand(bashCommand.ToString());
                    bashCommand.Clear();
                }
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
                if (session.FileExists(scratchDir + parameters.RunID + @"/complete"))
                {
                    AuxillaryFunctions.WriteLog(@"Analysis complete. Retrieving data...", parameters.LocalLogFilename, 0, false, parameters);

                    // Download files and throw on any error
                    session.GetFiles(scratchDir + parameters.RunID + @"/" + parameters.RunID + "_Filtered_Annotated.vcf", localAnalysisDir + @"\").Check();
                    session.GetFiles(scratchDir + parameters.RunID + @"/BAMsforDepthAnalysis.list", localAnalysisDir + @"\").Check();
                    session.GetFiles(scratchDir + parameters.RunID + @"/" + parameters.RunID + @"_Coverage.txt", localAnalysisDir + @"\").Check();
                    session.GetFiles(scratchDir + parameters.RunID + @"/" + parameters.PreferredTranscriptsFile, localAnalysisDir + @"\").Check();
                    session.GetFiles(scratchDir + parameters.RunID + @"/*.bed", localAnalysisDir + @"\").Check();
                    session.GetFiles(scratchDir + parameters.RunID + @"/*.sh", localAnalysisDir + @"\").Check();
                    session.GetFiles(scratchDir + parameters.RunID + @"/*.config", localAnalysisDir + @"\").Check();
                    // If <parameters.runID>_genecoverage.zip file exists download it
                    try
                    {                        
                        session.GetFiles(scratchDir + parameters.RunID + @"/" + parameters.RunID + @"_genecoverage.zip", localAnalysisDir + @"\").Check();
                        // And move it to the network
                        if (parameters.CopyToNetwork)
                        {
                            File.Copy(localAnalysisDir + @"\" + parameters.RunID + @"_genecoverage.zip", networkAnalysisDir + @"\" + parameters.RunID + "_genecoverage.zip");
                        }
                    }
                    catch
                    {
                        // If it doesn't exist, catch the error and log as this file is not essential (for this step of the process)
                        AuxillaryFunctions.WriteLog(@"No genecoverage.zip file found", parameters.LocalLogFilename, 1, false, parameters);
                    }
                    // Download a single copy of the scripts from the first sample
                    // This is needed as we don't have a copy of all scripts in the root run folder.
                    session.GetFiles(scratchDir + parameters.RunID + @"/" + sampleSheet.SampleRecords[1].Sample_ID + @"/*.sh", localAnalysisDir + @"\").Check();

                    // Copy to network
                    if (parameters.CopyToNetwork)
                    {
                        File.Copy(localAnalysisDir + @"\" + parameters.RunID + "_Filtered_Annotated.vcf", networkAnalysisDir + @"\" + parameters.RunID + "_Filtered_Annotated.vcf");
                        File.Copy(localAnalysisDir + @"\BAMsforDepthAnalysis.list", networkAnalysisDir + @"\BAMsforDepthAnalysis.list");
                        File.Copy(localAnalysisDir + @"\" + parameters.RunID + "_Coverage.txt", networkAnalysisDir + @"\" + parameters.RunID + "_Coverage.txt");
                        File.Copy(localAnalysisDir + @"\" + parameters.PreferredTranscriptsFile, networkAnalysisDir + @"\" + parameters.PreferredTranscriptsFile);

                        // Copy multiple files to the network - I think this might be because File.Copy only does single files?
                        foreach (var f in Directory.GetFiles(localAnalysisDir).Where(path => Regex.Match(path, @".*.bed").Success)) { File.Copy(f, networkAnalysisDir + @"\" + Path.GetFileName(f)); }
                        foreach (var f in Directory.GetFiles(localAnalysisDir).Where(path => Regex.Match(path, @".*.sh").Success)) { File.Copy(f, networkAnalysisDir + @"\" + Path.GetFileName(f)); }
                        foreach (var f in Directory.GetFiles(localAnalysisDir).Where(path => Regex.Match(path, @".*.config").Success)) { File.Copy(f, networkAnalysisDir + @"\" + Path.GetFileName(f)); }
                    }

                    return true;
                }
                // If the run doesn't appear to have finished, display a message so the user knows this.
                else
                {
                    AuxillaryFunctions.WriteLog(@"Marker File Not Found. Run is Pending", parameters.LocalLogFilename, 0, false, parameters);
                    return false;
                }
            }
        }

        /// <summary>
        /// Write the local report file, summarising the variants detected in every sample.
        /// </summary>
        private void WritePanelReport()
        {
            AuxillaryFunctions.WriteLog(@"Writing panel report...", parameters.LocalLogFilename, 0, false, parameters);

            string[] hgvs;
            //GenomicVariant tempGenomicVariant;
            StreamWriter panelReport = new StreamWriter(localReportFilename);
            ParseVCF VCFFile = new ParseVCF(localAnalysisDir + @"\" + parameters.RunID + "_Filtered_Annotated.vcf", parameters);
            ParseVCF BCIntFile = new ParseVCF(parameters.InterpretationsFile, parameters);
            Dictionary<GenomicVariant, string> interpretations = new Dictionary<GenomicVariant, string>();

            panelReport.WriteLine("SampleID\tSampleName\tGene\tHGVSc\tHGVSp\tExon\tGenotype\tTranscriptID\tFunction\tInterpretation\tHTSFAmplicon\tChromosome\tPosition\tReference\tAlternative");

            //get failed amplicons
            AnalyseCoverageData(localAnalysisDir + @"\" + parameters.RunID + "_Coverage.txt", localAnalysisDir + @"\BAMsforDepthAnalysis.list");

            //load BC ints
            //DEV
            AuxillaryFunctions.WriteLog(@"Loading BCInterpretations file...", parameters.LocalLogFilename, 0, false, parameters);
            foreach (VCFRecordWithGenotype record in BCIntFile.VCFRecords[""]) //loop over interpretations
            {
                GenomicVariant tempGenomicVariant = new GenomicVariant(CHROM: record.CHROM, POS: record.POS, REF: record.REF, ALT: record.ALT);
                //tempGenomicVariant.CHROM = record.CHROM;
                //tempGenomicVariant.POS = record.POS;
                //tempGenomicVariant.REF = record.REF;
                //tempGenomicVariant.ALT = record.ALT;

                // DEV:
                try
                {
                    interpretations.Add(tempGenomicVariant, record.INFO["INT"]);
                }
                catch
                {
                    AuxillaryFunctions.WriteLog(@"Could not find INT parameter...", parameters.LocalLogFilename, 1, false, parameters);
                    try
                    {
                        interpretations.Add(tempGenomicVariant, record.INFO["EFF"]);
                    }
                    catch
                    {
                        AuxillaryFunctions.WriteLog(@"Could not find EFF parameter...", parameters.LocalLogFilename, 1, false, parameters);
                        throw;
                    }
                }
            }

            //loop over samples for panel analysis
            //DEV
            AuxillaryFunctions.WriteLog(@"Check samples...", parameters.LocalLogFilename, 0, false, parameters);
            foreach (SampleRecord record in sampleSheet.SampleRecords)
            {
                if (record.Analysis != @"P") {
                    continue;
                }

                if (!VCFFile.VCFRecords.ContainsKey(record.Sample_ID))
                {
                    AuxillaryFunctions.WriteLog(@"Sample " + record.Sample_ID + " has no genotypes in panel VCF", parameters.LocalLogFilename, -1, false, parameters);
                    throw new FileLoadException();
                }

                //loop over VCF records for this sample
                foreach (VCFRecordWithGenotype VCFrecord in VCFFile.VCFRecords[record.Sample_ID])
                {
                    GenomicVariant tempGenomicVariant = new GenomicVariant(CHROM: VCFrecord.CHROM, POS: VCFrecord.POS, REF: VCFrecord.REF, ALT: VCFrecord.ALT);
                    //tempGenomicVariant.CHROM = VCFrecord.CHROM;
                    //tempGenomicVariant.REF = VCFrecord.REF;
                    //tempGenomicVariant.ALT = VCFrecord.ALT;
                    //tempGenomicVariant.POS = VCFrecord.POS;

                    if (VCFrecord.FORMAT["GT"] == @"0/0") //skip hom ref variants
                    {
                        continue;
                    }

                    if (VCFFile.SnpEffAnnotations.ContainsKey(tempGenomicVariant)) //annotation is available
                    {
                        //loop over annotations
                        foreach (Annotation ann in VCFFile.SnpEffAnnotations[tempGenomicVariant])
                        {
                            hgvs = ann.Amino_Acid_Change.Split('/');

                            panelReport.Write(record.Sample_ID + "\t");
                            panelReport.Write(record.Sample_Name + "\t");
                            panelReport.Write(ann.Gene_Name + "\t");

                            if (hgvs.Length == 2) { //c. and p.
                                panelReport.Write(hgvs[1] + "\t"); //c.
                                panelReport.Write(hgvs[0] + "\t"); //p.
                            }
                            else
                            {
                                panelReport.Write(hgvs[0] + "\t"); //c.
                                panelReport.Write("\t");
                            }

                            //print exon
                            panelReport.Write("{0}\t", ann.Exon_Rank);

                            // 1/0 is not technically VCF spec, but is output by some tools so we should account for it
                            if (VCFrecord.FORMAT["GT"] == @"0/1" || VCFrecord.FORMAT["GT"] == @"1/0")
                            {
                                panelReport.Write("HET\t");
                            }
                            else if (VCFrecord.FORMAT["GT"] == @"1/1") {
                                panelReport.Write("HOM_ALT\t");
                            }
                            // uncertain genotypes (i.e. with ".") should be flagged.
                            // in all likelihood they will be flagged as GAPs, but best to be sure
                            else if (VCFrecord.FORMAT["G"] == @"./1" || VCFrecord.FORMAT["G"] == @"1/.")
                            {
                                panelReport.Write("UNCERTAIN_HET\t");
                            }
                            // homozygous uncertain genotypes are almost certainly gaps, and are filtered by the current pipeline
                            // but for futureproofing/compatibility with other pipelines they are included
                            else if (VCFrecord.FORMAT["GT"] == @"./.")
                            {
                                panelReport.Write("UNCERTAIN_HOM\t");
                            }
                            else if (VCFrecord.FORMAT["GT"] == @"")
                            {
                                panelReport.Write("Unknown\t");
                            }
                            else
                            {
                                panelReport.Write("Complex\t");
                            }

                            panelReport.Write(ann.Transcript_ID + "\t");
                            panelReport.Write(ann.Effect + "\t");

                            if (interpretations.ContainsKey(tempGenomicVariant)) {
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
                    else //no annotation
                    {
                        panelReport.Write(record.Sample_ID + "\t");
                        panelReport.Write(record.Sample_Name + "\t");
                        panelReport.Write("\t\t\t\t");

                        // 1/0 is not technically VCF spec, but is output by some tools so we should account for it
                        if (VCFrecord.FORMAT["GT"] == @"0/1" || VCFrecord.FORMAT["GT"] == @"1/0")
                        {
                            panelReport.Write("HET\t");
                        }
                        else if (VCFrecord.FORMAT["GT"] == @"1/1")
                        {
                            panelReport.Write("HOM_ALT\t");
                        }
                        // uncertain genotypes (i.e. with ".") should be flagged.
                        // in all likelihood they will be flagged as GAPs, but best to be sure
                        else if (VCFrecord.FORMAT["G"] == @"./1" || VCFrecord.FORMAT["G"] == @"1/.")
                        {
                            panelReport.Write("UNCERTAIN_HET\t");
                        }
                        // homozygous uncertain genotypes are almost certainly gaps, and are filtered by the current pipeline
                        // but for futureproofing/compatibility with other pipelines they are included
                        else if (VCFrecord.FORMAT["GT"] == @"./.")
                        {
                            panelReport.Write("UNCERTAIN_HOM\t");
                        }
                        else
                        {
                            panelReport.Write("OTHER\t");
                        }

                        panelReport.Write("\t\t");

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

                } //done looping over VCF Records

                //add gaps
                if (!failedAmplicons.ContainsKey(record.Sample_ID))
                {
                    AuxillaryFunctions.WriteLog(@"Sample " + record.Sample_ID + " coverage data not loaded successfully!", parameters.LocalLogFilename, -1, false, parameters);
                    throw new FileLoadException();
                }

                //print failed regions
                foreach (string failedCoreRegion in failedAmplicons[record.Sample_ID])
                {
                    panelReport.WriteLine(record.Sample_ID + "\t" + record.Sample_Name + "\t\t\t\t\t\t\t\tGAP\t" + failedCoreRegion);
                }

            } //done looping over SampleIDs

            panelReport.Close();
        }

        /// <summary>
        /// Work out which amplicons contain gaps below the minimum required coverage 
        /// </summary>
        /// <param name="samtoolsDepthFilePath">_Coverage file for the run</param>
        /// <param name="samtoolsDepthSampleIDFilePath">CoverageSampleOrder.txt file</param>
        private void AnalyseCoverageData(string samtoolsDepthFilePath, string samtoolsDepthSampleIDFilePath)
        {
            AuxillaryFunctions.WriteLog(@"Analysing coverage data...", parameters.LocalLogFilename, 0, false, parameters);

            string line, failedAmpliconID;
            int pos;
            Dictionary<Tuple<string, int>, bool> isBaseCovered = new Dictionary<Tuple<string, int>, bool>(); //bool = observed
            List<string> sampleIDs = new List<string>();

            //read sampleID order
            using (FileStream stream = new FileStream(samtoolsDepthSampleIDFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
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
            {
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
            AuxillaryFunctions.WriteLog(@"Analysing coverage data complete.", parameters.LocalLogFilename, 0, false, parameters);
        }

        /// <summary>
        /// Writes variables file for each sample. Variables file contains details requried for Linux analysis scripts.
        /// </summary>
        private void WriteVariablesFiles()
        {
            AuxillaryFunctions.WriteLog(@"Writing variable files...", parameters.LocalLogFilename, 0, false, parameters);

            //concatinate analysisdirs
            StringBuilder AnalysisDirs = new StringBuilder(@"AnalysisDirs=( ");

            foreach (SampleRecord record in sampleSheet.SampleRecords)
            {
                if (record.Analysis == @"P") {
                    AnalysisDirs.Append('"');
                    AnalysisDirs.Append(scratchDir);
                    AnalysisDirs.Append(parameters.RunID);
                    AnalysisDirs.Append('/');
                    AnalysisDirs.Append(record.Sample_ID);
                    AnalysisDirs.Append('"');
                    AnalysisDirs.Append(' ');
                }
            }
            AnalysisDirs.Append(')');

            foreach (SampleRecord record in sampleSheet.SampleRecords)
            {
                if (record.Analysis != @"P") {
                    continue;
                }

                //open variables output file
                StreamWriter VarFile = new StreamWriter(localAnalysisDir + @"\" + record.Sample_ID + @".variables");

                VarFile.Write("#Description: Pipeline Variables File\n");

                VarFile.Write("\n#Sample_ID\n");
                VarFile.Write(@"Sample_ID=" + record.Sample_ID + "\n");

                VarFile.Write("\n#FASTQ MD5 checksum\n");
                VarFile.Write(@"R1MD5Filename=" + Path.GetFileName(fastqFileNames[record.Sample_ID].Item1) + ".md5\n");
                VarFile.Write(@"R2MD5Filename=" + Path.GetFileName(fastqFileNames[record.Sample_ID].Item2) + ".md5\n");

                VarFile.Write("\n#FASTQ filenames\n");
                VarFile.Write(@"R1Filename=" + Path.GetFileName(fastqFileNames[record.Sample_ID].Item1) + "\n");
                VarFile.Write(@"R2Filename=" + Path.GetFileName(fastqFileNames[record.Sample_ID].Item2) + "\n");

                VarFile.Write("\n#Capture ROI\n");
                VarFile.Write(@"BEDFilename=" + Path.GetFileName(sampleSheet.Analyses[@"P"]) + "\n");

                VarFile.Write("\n#RunDetails\n");
                VarFile.Write(@"RunID=" + parameters.RunID + "\n");
                VarFile.Write(@"ExperimentName=" + sampleSheet.ExperimentName + "\n");
                VarFile.Write("Platform=ILLUMINA\n");

                VarFile.Write("\n#Annotation\n");
                VarFile.Write(@"PreferredTranscriptsFile=" + scratchDir + parameters.RunID + @"/" + Path.GetFileName(parameters.PreferredTranscriptsFile) + "\n");

                VarFile.Write("\n#AnalysisFolders\n");
                VarFile.Write(AnalysisDirs.ToString() + "\n");

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
            AuxillaryFunctions.WriteLog(@"Starting BAM downloader...", parameters.LocalLogFilename, 0, false, parameters);
            // Connect to Iridis
            // TODO: Write ConnectToIridis function that returns the new Session object used here?

            // Create the run folder in the Panels BAM store
            string RunBamStore = $@"{parameters.BamStoreLocation}\Panels\{parameters.RunID}";

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
                    string BamRootFileRemote = $@"{scratchDir}/{parameters.RunID}/{record.Sample_ID}/{parameters.RunID}_{record.Sample_ID}";
                    string BamRootFileLocal = $@"{RunBamStore}\{parameters.RunID}_{record.Sample_ID}";

                    AuxillaryFunctions.WriteLog($@"Download {BamRootFileRemote} to {BamRootFileLocal}...", parameters.LocalLogFilename, 0, false, parameters);

                    if ( ! File.Exists($@"{BamRootFileLocal}.bam"))
                    {
                        session.GetFiles($@"{BamRootFileRemote}.bam", $@"{BamRootFileLocal}.bam", false, transferOptions).Check();
                    }
                    session.GetFiles($@"{BamRootFileRemote}.bai", $@"{BamRootFileLocal}.bai", false, transferOptions).Check();
                }
            }
        }

        /// <summary>
        /// Creates a Session object connected to iridis, so this functionality doens't need to be repeated.
        /// </summary>
        /// <returns></returns>
        private Session ConnectToIridis()
        {
            // DEV: We might also want to put the original iridis4a,b,c settings instantiation in here, but only
            // once we start to transfer the other steps to use this function.

            // Create the session object and set up the WinSCP logging file

            SessionOptions iridis4SessionOptions = new SessionOptions
            {
                Protocol = Protocol.Sftp,
                HostName = @"iridis4_a.soton.ac.uk",
                UserName = parameters.SotonUserName,
                Password = ProgrammeParameters.ToInsecureString(parameters.SotonPassword),
                SshHostKeyFingerprint = parameters.IridisHostKey,
            };
            iridis4SessionOptions.AddRawSettings(@"Tunnel", @"1");
            iridis4SessionOptions.AddRawSettings(@"TunnelHostName", @"ssh.soton.ac.uk");
            iridis4SessionOptions.AddRawSettings(@"TunnelUserName", parameters.SotonUserName);
            iridis4SessionOptions.AddRawSettings(@"TunnelPasswordPlain", ProgrammeParameters.ToInsecureString(parameters.SotonPassword));
            iridis4SessionOptions.AddRawSettings(@"TunnelHostKey", parameters.SSHHostKey);

            Session session = new Session
            {
                SessionLogPath = winscpLogPath
            };

            // Try to connect to each Iridis login node in turn, in case one of them is down
            // Connect to iridis4a
            try
            {
                iridis4SessionOptions.HostName = @"iridis4_a.soton.ac.uk";
                AuxillaryFunctions.WriteLog($@"Connecting To {iridis4SessionOptions.HostName}...", parameters.LocalLogFilename, 0, false, parameters);
                session.Open(iridis4SessionOptions);
            }
            catch (WinSCP.SessionRemoteException a)
            {
                AuxillaryFunctions.WriteLog(@"Could not connect: " + a.ToString(), parameters.LocalLogFilename, 1, false, parameters);
                // Connect to iridis4b
                try
                {
                    iridis4SessionOptions.HostName = @"iridis4_b.soton.ac.uk";
                    AuxillaryFunctions.WriteLog(@"Connecting To {iridis4SessionOptions.HostName}...", parameters.LocalLogFilename, 0, false, parameters);
                    session.Open(iridis4SessionOptions);
                }
                catch (WinSCP.SessionRemoteException b)
                {
                    AuxillaryFunctions.WriteLog(@"Could not connect: " + b.ToString(), parameters.LocalLogFilename, 1, false, parameters);
                    // Connect to iridis4c
                    try
                    {
                        iridis4SessionOptions.HostName = @"iridis4_c.soton.ac.uk";
                        AuxillaryFunctions.WriteLog(@"Connecting To {iridis4SessionOptions.HostName}...", parameters.LocalLogFilename, 0, false, parameters);
                        session.Open(iridis4SessionOptions);
                    }
                    catch (WinSCP.SessionRemoteException c)
                    {
                        AuxillaryFunctions.WriteLog(@"Could not connect: " + c.ToString(), parameters.LocalLogFilename, -1, false, parameters);
                        // This error is not caught, and so will end the pipeline as we can't connect to any login node
                        throw;
                    }
                }
            }
            // If we reach here then we should have a correctly opened session!
            return session;
        }
    }
}
