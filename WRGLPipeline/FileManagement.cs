using System;
using System.Linq;
using System.Text;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace WRGLPipeline
{
    class FileManagement
    {
        /// <summary>
        /// Copy run files to a network location. Files are organised so that run can easily be repeated.
        /// </summary>
        /// <param name="parameters">Configured ProgrammeParameters</param>
        public static void BackupFiles(ProgrammeParameters parameters)
        {
            string[] FASTQfiles = Directory.GetFiles(parameters.LocalFastqDir, @"*.fastq.gz");

            // Compute MD5 and copy FASTQ files and checksums to network location
            // fastqs (and md5s) should be in <runID>\Data\Intensities\BaseCalls
            
            // Create BaseCalls folder
            Directory.CreateDirectory($@"{parameters.NetworkRootRunDir}\Data\Intensities\BaseCalls");
            
            // Loop over files and copy
            foreach (string FASTQfilePath in FASTQfiles){

                string FASTQFilename = Path.GetFileName(FASTQfilePath);
                string[] FASTQfilenameFields = FASTQFilename.Split('_');

                // Skip unindexed data
                if (FASTQfilenameFields[0] != @"Undetermined")
                {
                    

                    AuxillaryFunctions.WriteLog($@"Calculating MD5 checksum for {FASTQFilename}", parameters);
                    // Set the line ending in the StreamWriter options
                    using (StreamWriter MD5File = new StreamWriter($"{FASTQfilePath}.md5") { NewLine = "\n" })
                    {
                        MD5File.WriteLine($@"{FileManagement.GetMD5HashFromFile(FASTQfilePath)} {FASTQFilename}");
                    }

                    AuxillaryFunctions.WriteLog($@"Copying files to network: {FASTQFilename} {FASTQFilename}.md5", parameters);

                    File.Copy(FASTQfilePath, $@"{parameters.NetworkRootRunDir}\Data\Intensities\BaseCalls\{FASTQFilename}");
                    File.Copy($@"{FASTQfilePath}.md5", $@"{parameters.NetworkRootRunDir}\Data\Intensities\BaseCalls\{FASTQFilename}.md5");
                }
            }

            // Copy run files to network
            AuxillaryFunctions.WriteLog(@"Copying SampleSheet and performance metrics to network", parameters);

            Directory.CreateDirectory($@"{parameters.NetworkRootRunDir}\Data\Intensities\BaseCalls\Alignment");
            File.Copy($@"{parameters.SuppliedDir}\SampleSheetUsed.csv", $@"{parameters.NetworkRootRunDir}\SampleSheetUsed.csv");
            File.Copy($@"{parameters.SuppliedDir}\SampleSheetUsed.csv", $@"{parameters.NetworkRootRunDir}\Data\Intensities\BaseCalls\Alignment\SampleSheetUsed.csv");
            File.Copy($@"{parameters.SuppliedDir}\DemultiplexSummaryF1L1.txt", $@"{parameters.NetworkRootRunDir}\Data\Intensities\BaseCalls\Alignment\DemultiplexSummaryF1L1.txt");

            // These go in root run folder, so this is ok
            File.Copy($@"{parameters.LocalRootRunDir}\runParameters.xml", $@"{parameters.NetworkRootRunDir}\runParameters.xml");
            File.Copy($@"{parameters.LocalRootRunDir}\RunInfo.xml", $@"{parameters.NetworkRootRunDir}\RunInfo.xml");

            // InterOp is correctly placed in the root run folder
            Directory.CreateDirectory($@"{parameters.NetworkRootRunDir}\InterOp");
            FileManagement.DirectoryCopyFunction($@"{parameters.LocalRootRunDir}InterOp", $@"{parameters.NetworkRootRunDir}\InterOp", parameters, false);
        }

        /// <summary>
        /// Delete all but the 10 most recent runs on the local (i.e. non-network) analysis directory
        /// </summary>
        /// <param name="parameters">Configured ProgrammeParameters</param>
        public static void DeleteOldRuns(ProgrammeParameters parameters)
        {
            // Get directory list and sort by date creation
            var di = new DirectoryInfo(parameters.LocalMiSeqAnalysisDir);
            var directories = di.EnumerateDirectories()
                                .OrderBy(d => d.CreationTime)
                                .Select(d => d.Name)
                                .ToList();

            // Delete old runs; protect the last 10 (newest) runs
            for (int n = 0; n < directories.Count - 10; ++n)
            {
                if (directories[n] != @"Temp")
                {
                    ForceDeleteDirectory(parameters.LocalMiSeqAnalysisDir + directories[n], parameters);
                }
            }

            // Delete MiSeqOutput for this run only
            // DEV: This probably shouldn't be hard-coded!!
            string[] outputSubfolders = Directory.GetDirectories(@"D:\Illumina\MiSeqOutput");
            string outputRunDir = $@"D:\Illumina\MiSeqOutput\{parameters.RunID}";
            ForceDeleteDirectory(outputRunDir, parameters);
        }

        /// <summary>
        /// Deletes a folder even if it contains read-only files
        /// </summary>
        /// <param name="path">Path to the folder to be deleted.</param>
        /// <param name="parameters">Programme parameters object</param>
        public static void ForceDeleteDirectory(string path, ProgrammeParameters parameters) 
        {
            AuxillaryFunctions.WriteLog($@"Deleting folder: {path}", parameters);
            //TODO Might want to move the try/catch around deleting folders in to here?
            //     Would have to sort out how best to reference the logFilename etc. first
            var directory = new DirectoryInfo(path) { Attributes = FileAttributes.Normal };
            // Set all files in the directory to Normal (i.e. not read only)
            foreach (var info in directory.GetFileSystemInfos("*", SearchOption.AllDirectories))
            {
                info.Attributes = FileAttributes.Normal;
            }
            // Then delete them
            try
            {
                directory.Delete(true);
            }
            catch (Exception e)
            {
                // Catch the error and log, but don't re-throw - allow to continue.
                AuxillaryFunctions.WriteLog($@"Could not delete folder: {path}\n{e.ToString()}", parameters, errorCode: -1);
            }
        }

        /// <summary>
        /// Calculates the MD5 hash of a file using Microsoft crypto library
        /// </summary>
        /// <param name="fileName">File to hash</param>
        /// <returns>String representing the MD5 hash value of the file</returns>
        public static string GetMD5HashFromFile(string fileName)
        {
            FileStream file = new FileStream(fileName, FileMode.Open);
            MD5 md5 = new MD5CryptoServiceProvider();
            byte[] retVal = md5.ComputeHash(file);
            file.Close();

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < retVal.Length; i++)
            {
                sb.Append(retVal[i].ToString("x2"));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Recursively copy a directory and all subdirectories to another location
        /// </summary>
        /// <param name="sourceDirName">Directory to copy</param>
        /// <param name="destDirName">Location to copy to</param>
        /// <param name="parameters">Configured ProgrammeParameters</param>
        /// <param name="copySubDirs">Optionally disable copying all subdirectories</param>
        public static void DirectoryCopyFunction(string sourceDirName, string destDirName, ProgrammeParameters parameters, bool copySubDirs = true)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);
            DirectoryInfo[] dirs = dir.GetDirectories();

            if (!dir.Exists)
            {
                AuxillaryFunctions.WriteLog($@"Source directory does not exist or could not be found {sourceDirName}", parameters, errorCode: -1);
                throw new DirectoryNotFoundException();
            }

            // If the destination directory doesn't exist, create it. 
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(destDirName, file.Name);
                file.CopyTo(temppath, false);
            }

            // If copying subdirectories, copy them and their contents to new location. 
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string temppath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopyFunction(subdir.FullName, temppath, parameters);
                }
            }
        }

        /// <summary>
        /// Prepare an LRM style folder - prep to resemle the older MSR style
        /// this lets us handle both cases (although going forward we'd expect all
        /// LRM style - so could change up for that.
        /// NOTE: cannot use parameters as this is called from within ProgrammeParameters itself
        /// </summary>
        /// <param name="alignmentDir">Path to the run alignment folder</param>
        public static string PrepLRMRun(string alignmentDir)
        {
            // Create the correct Alignment folder
            // For a LRM run the passed alignmentDir should be the alignment_1 folder, so the
            // parent of that is the root run directory
            string runFolder = Directory.GetParent(Directory.GetParent(alignmentDir).FullName).FullName;
            string newFastqFolder = $@"{runFolder}\Data\Intensities\Basecalls";
            string newAlignmentFolder = $@"{newFastqFolder}\Alignment";
            Directory.CreateDirectory(newAlignmentFolder);

            // There should be a subfolder of the LRM Alignment folder with the date of analysis
            // This contains the other needed files (e.g. fastqs, SampleSheet)
            // Get this folder and then copy these files to the new alignment and fastq folders
            string fastqFolder = $@"{alignmentDir}\Fastq";

            // Copy the FASTQs
            string pattern = @"\.fastq.gz";
            var files = Directory.GetFiles(fastqFolder).Where(x => Regex.IsMatch(x, pattern)).Select(x => x).ToList();
            foreach (var item in files)
            {
                string fastqfilename = Path.GetFileName(item);
                System.IO.File.Copy(item, $@"{newFastqFolder}\{fastqfilename}");
            };

            // Copy the remaining needed files
            System.IO.File.Copy($@"{alignmentDir}\SampleSheetUsed.csv", $@"{newAlignmentFolder}\SampleSheetUsed.csv");
            System.IO.File.Copy($@"{alignmentDir}\DemultiplexSummaryF1L1.txt", $@"{newAlignmentFolder}\DemultiplexSummaryF1L1.txt");

            // return the new alignment directory, so the rest of the process can
            // proceed as before
            return newAlignmentFolder;
        }
    }
}
