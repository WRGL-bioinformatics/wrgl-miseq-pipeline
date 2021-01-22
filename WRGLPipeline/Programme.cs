using System;
using System.Collections.Generic;
using System.IO;

namespace WRGLPipeline
{
    class Programme
    {
        /// <summary>
        /// Overall pipeline version number - must be manually updated and should match GitLab tags.
        /// DEV: might want to set as a string rather than double, to allow major.minor.increment format.
        /// </summary>
        public const double WRGLversion = 2.3;
        
        /// <summary>
        /// Main function
        /// </summary>
        /// <param name="args">System arguments from command line</param>
        private static void Main(string[] args)
        {
            try
            {
                // ProgrammeParameters holds all relevant parameters for the pipeline
                // This includes those read from the .ini config file and command line arguments
                // Any parameters derived from these values (except for panel/genotyping-specific ones)
                // are also created by the ProgrammeParameters constructor.
                // DEV: TODO: Ensure that all variables have appropriate properties
                Console.WriteLine("DEV: Reading args...");
                ProgrammeParameters parameters = new ProgrammeParameters(args);

                // Parse samplesheet (path defined in parameters)
                // DEV: For testing purposes it might actually be easier to explicitly pass in the
                //      samplesheet path?
                Console.WriteLine("DEV: Reading parameters...");
                ParseSampleSheet sampleSheet = new ParseSampleSheet(parameters);

                // Write these parameters to the logfile (for reference if needed)
                // DEV: localLogFilename should be in parameters
                //      and the "0" log code should probably be the default...
                //      and the first run boolean should probably also be handled by the log function...
                //      Also parameters should be set when the logger is created. And the logger should be a class/object.
                AuxillaryFunctions.WriteLog(@"Run identifier: " + parameters.RunID, parameters.LocalLogFilename, 0, true, parameters);
                AuxillaryFunctions.WriteLog(@"Target BED file: " + parameters.CoreBedFile, parameters.LocalLogFilename, 0, false, parameters);
                AuxillaryFunctions.WriteLog(@"Local FASTQ directory: " + parameters.LocalFastqDir, parameters.LocalLogFilename, 0, false, parameters);
                AuxillaryFunctions.WriteLog(@"Local MiSeq analysis directory: " + parameters.LocalMiSeqAnalysisDir, parameters.LocalLogFilename, 0, false, parameters);
                AuxillaryFunctions.WriteLog(@"Local output directory: " + parameters.LocalRootRunDir, parameters.LocalLogFilename, 0, false, parameters);
                AuxillaryFunctions.WriteLog(@"Network output directory: " + parameters.NetworkRootRunDir, parameters.LocalLogFilename, 0, false, parameters);
                AuxillaryFunctions.WriteLog(@"Local SampleSheet path: " + parameters.SampleSheetPath, parameters.LocalLogFilename, 0, false, parameters);
                AuxillaryFunctions.WriteLog(@"Experiment name: " + sampleSheet.ExperimentName, parameters.LocalLogFilename, 0, false, parameters);
                AuxillaryFunctions.WriteLog(@"Investigator name: " + sampleSheet.InvestigatorName, parameters.LocalLogFilename, 0, false, parameters);
                AuxillaryFunctions.WriteLog(@"GetData mode: " + parameters.GetData, parameters.LocalLogFilename, 0, false, parameters);
                AuxillaryFunctions.WriteLog(@"Copy to network: " + parameters.CopyToNetwork, parameters.LocalLogFilename, 0, false, parameters);

                // Create the network folder for the run (unless set not to copy to the network)
                // Do this in all cases - both full and get data.
                if (parameters.CopyToNetwork)
                {
                    //AuxillaryFunctions.MakeNetworkOutputDir(parameters.NetworkRootRunDir);
                    parameters.NetworkRootRunDir = AuxillaryFunctions.MakeNetworkOutputDir(parameters.NetworkRootRunDir);

                    // We don't want to run BackupFiles if it's a getData only run
                    if (!parameters.GetData)
                    {
                        // Compute MD5 for fastqs; copy fastqs, metrics, samplesheet and MD5 to network
                        FileManagement.BackupFiles(parameters);
                    }
                }

                // Check analysis type and run appropriate wrapper
                if (sampleSheet.Analyses.Count > 0)
                {
                    Dictionary<string, Tuple<string, string>> fastqFileNames = new Dictionary<string, Tuple<string, string>>(GetFASTQFileNames(sampleSheet, parameters));

                    // Analyse samples with the correct wrapper class depending on the samplesheet contents
                    if (sampleSheet.Analyses.ContainsKey("G"))
                    {
                        new GenotypingPipelineWrapper(sampleSheet, parameters, fastqFileNames);
                    }
                    else if (sampleSheet.Analyses.ContainsKey("P"))
                    {
                        new PanelPipelineWrapper(sampleSheet, parameters, fastqFileNames);
                    }
                    else if (sampleSheet.Analyses.ContainsKey("A"))
                    {
                        // "A" analyses will be analysed by MiSeq Reporter, but this should still run on completion.
                        // Most are likely to be myeloid panel runs, which we want to copy to the network and create
                        // coverage summaries for automatically.
                        // Assume that any "A" is myeloid and run the wrapper. If it's not right, it won't matter as this is non-destructive.
                        new MyeloidPipelineWrapper(sampleSheet, parameters, fastqFileNames);
                    }
                    else
                    {
                        // Log that the analysis type wasn't recognised, and then allow to run as this should reach the end and close
                        AuxillaryFunctions.WriteLog("Analysis type was not recognised.", parameters.LocalLogFilename, -1, false, parameters);
                    }
                }

                // Delete local oldest run
                if (parameters.DeleteOldestLocalRun == true)
                {
                    FileManagement.DeleteOldRuns(parameters);
                }

                // Copy log file to network
                if (parameters.CopyToNetwork)
                {
                    // DEV: TODO: create a "networkLogFilename" using these settings.
                    File.Copy(parameters.LocalLogFilename, parameters.NetworkRootRunDir + @"\" + Path.GetFileName(parameters.LocalLogFilename)); //logfile
                }

                // If complete, write to the log
                AuxillaryFunctions.WriteLog("Analysis completed.", parameters.LocalLogFilename, 0, false, parameters);
            }
            catch (Exception e)
            {
                try
                {
                    // We have to re-load the parameters here, as the previous try block is out of scope
                    ProgrammeParameters parameters = new ProgrammeParameters(args);
                    AuxillaryFunctions.WriteLog("An error occured. Exception details as follows:", parameters.LocalLogFilename, -1, false, parameters);
                    AuxillaryFunctions.WriteLog(e.ToString(), parameters.LocalLogFilename, -1, false, parameters);
                }
                catch
                {
                    // If an error occured above, then we probably can't write to the specified log file.
                    // Instead, write to the console.
                    Console.WriteLine("ERROR: An error occured. Exception details as follows:");
                    Console.WriteLine("");
                    Console.WriteLine(e.ToString());
                }
            }
        }

        /// <summary>
        /// Find FASTQ file names for each sample by searching target directory based on their sample ID
        /// </summary>
        /// <param name="sampleSheet">Parsed SampleSheet for this run</param>
        /// <param name="parameters">Configure ProgrammeParameters</param>
        /// <returns>Dictionary of sample IDs and their FASTQ file names</returns>
        private static Dictionary<string, Tuple<string, string>> GetFASTQFileNames(ParseSampleSheet sampleSheet, ProgrammeParameters parameters)
        {
            Dictionary<string, Tuple<string, string>> fastqFileNames = new Dictionary<string, Tuple<string, string>>();
            // While we only expect to find a single file each for R1 and R2, the Directory.GetFiles method only
            // returns an array, regardless of number of results.
            string[] read1Files, read2Files;

            foreach (SampleRecord record in sampleSheet.SampleRecords)
            {
                // Get FASTQ filenames - 
                if (record.Sample_Name == "")
                {
                    read1Files = Directory.GetFiles(parameters.LocalFastqDir, record.Sample_ID + @"_*_R1_*.fastq.gz");
                    read2Files = Directory.GetFiles(parameters.LocalFastqDir, record.Sample_ID + @"_*_R2_*.fastq.gz");
                }
                else
                {
                    read1Files = Directory.GetFiles(parameters.LocalFastqDir, record.Sample_Name + @"_*_R1_*.fastq.gz");
                    read2Files = Directory.GetFiles(parameters.LocalFastqDir, record.Sample_Name + @"_*_R2_*.fastq.gz");
                }
                // No paired-end FASTQs found for this Sample_ID
                // All samples MUST have read data, so throw an error if this happens
                if (read1Files.Length == 0 || read2Files.Length == 0)
                {
                    AuxillaryFunctions.WriteLog(@"Paired-end FASTQ file(s) not found for " + record.Sample_ID, parameters.LocalLogFilename, -1, false, parameters);
                    throw new FileNotFoundException();
                }
                // Store the results
                Tuple<string, string> fileNames = new Tuple<string, string>(read1Files[0], read2Files[0]);
                fastqFileNames.Add(record.Sample_ID, fileNames);
            }
            return fastqFileNames;
        }
    }
}

