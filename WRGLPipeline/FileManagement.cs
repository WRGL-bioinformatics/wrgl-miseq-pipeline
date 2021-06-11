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

            //compute MD5 and copy FASTQ files and checksums to network location
            //fastqs (and md5s) should be in <runID>\Data\Intensities\BaseCalls
            
            //create BaseCalls folder
            Directory.CreateDirectory(parameters.NetworkRootRunDir + @"\Data\Intensities\BaseCalls");
            
            //loop over files and copy
            foreach (string FASTQfilePath in FASTQfiles){

                string FASTQFilename = Path.GetFileName(FASTQfilePath);
                string[] FASTQfilenameFields = FASTQFilename.Split('_');

                if (FASTQfilenameFields[0] != @"Undetermined") //skip unindexed data
                {
                    StreamWriter MD5File = new StreamWriter(FASTQfilePath + @".md5");

                    AuxillaryFunctions.WriteLog(@"Calculating MD5 checksum for " + FASTQFilename, parameters.LocalLogFilename, 0, false, parameters);

                    MD5File.Write(FileManagement.GetMD5HashFromFile(FASTQfilePath) + @"  " + FASTQFilename + "\n");
                    MD5File.Close();

                    AuxillaryFunctions.WriteLog(@"Copying files to network: " + FASTQFilename + ' ' + FASTQFilename + @".md5", parameters.LocalLogFilename, 0, false, parameters);

                    File.Copy(FASTQfilePath, parameters.NetworkRootRunDir + @"\Data\Intensities\BaseCalls\" + FASTQFilename);
                    File.Copy(FASTQfilePath + @".md5", parameters.NetworkRootRunDir + @"\Data\Intensities\BaseCalls\" + FASTQFilename + @".md5");
                }
            }

            //copy run files to network
            AuxillaryFunctions.WriteLog(@"Copying SampleSheet and performance metrics to network", parameters.LocalLogFilename, 0, false, parameters);

            Directory.CreateDirectory(parameters.NetworkRootRunDir + @"\Data\Intensities\BaseCalls\Alignment");
            File.Copy(parameters.SuppliedDir + @"\SampleSheetUsed.csv", parameters.NetworkRootRunDir + @"\SampleSheetUsed.csv");
            File.Copy(parameters.SuppliedDir + @"\SampleSheetUsed.csv", parameters.NetworkRootRunDir + @"\Data\Intensities\BaseCalls\Alignment\SampleSheetUsed.csv");
            File.Copy(parameters.SuppliedDir + @"\DemultiplexSummaryF1L1.txt", parameters.NetworkRootRunDir + @"\Data\Intensities\BaseCalls\Alignment\DemultiplexSummaryF1L1.txt");

            // these go in root run folder, so this is ok
            File.Copy(parameters.LocalRootRunDir + @"runParameters.xml", parameters.NetworkRootRunDir + @"\runParameters.xml");
            File.Copy(parameters.LocalRootRunDir + @"RunInfo.xml", parameters.NetworkRootRunDir + @"\RunInfo.xml");

            //InterOp is correctly placed in the root run folder
            Directory.CreateDirectory(parameters.NetworkRootRunDir + @"\InterOp");
            FileManagement.DirectoryCopyFunction(parameters.LocalRootRunDir + @"InterOp", parameters.NetworkRootRunDir + @"\InterOp", false, parameters);
        }

        /// <summary>
        /// Delete all but the 10 most recent runs on the local (i.e. non-network) analysis directory
        /// </summary>
        /// <param name="parameters">Configured ProgrammeParameters</param>
        public static void DeleteOldRuns(ProgrammeParameters parameters)
        {
            //get dir list and sort by date creation
            var di = new DirectoryInfo(parameters.LocalMiSeqAnalysisDir);
            var directories = di.EnumerateDirectories()
                                .OrderBy(d => d.CreationTime)
                                .Select(d => d.Name)
                                .ToList();

            //delete old runs; protect the last 10 (newest) runs
            for (int n = 0; n < directories.Count - 10; ++n)
            {
                if (directories[n] != @"Temp")
                {
                    try
                    {
                        AuxillaryFunctions.WriteLog(@"Deleting folder: " + parameters.LocalMiSeqAnalysisDir + directories[n], parameters.LocalLogFilename, 0, false, parameters);
                        // Use the custom ForceDeleteDirectory function to remove folders even if there are read only files present
                        ForceDeleteDirectory(parameters.LocalMiSeqAnalysisDir + directories[n]);
                    }
                    catch (Exception e)
                    {
                        AuxillaryFunctions.WriteLog(@"Could not delete folder: " + e.ToString(), parameters.LocalLogFilename, -1, false, parameters);
                    }
                }
            }

            //delete MiSeqOutput
            string[] outputSubfolders = Directory.GetDirectories(@"D:\Illumina\MiSeqOutput");
            foreach (string dir in outputSubfolders)
            {
                try
                {
                    AuxillaryFunctions.WriteLog(@"Deleting folder: " + dir, parameters.LocalLogFilename, 0, false, parameters);
                    ForceDeleteDirectory(dir);
                }
                catch (Exception e)
                {
                    AuxillaryFunctions.WriteLog(@"Could not delete folder: " + e.ToString(), parameters.LocalLogFilename, -1, false, parameters);
                }
            }

        }

        /// <summary>
        /// Deletes a folder even if it contains read-only files
        /// </summary>
        /// <param name="path">Path to the folder to be deleted.</param>
        public static void ForceDeleteDirectory(string path) 
        {
            //TODO Might want to move the try/catch around deleting folders in to here?
            //     Would have to sort out how best to reference the logFilename etc. first
            var directory = new DirectoryInfo(path) { Attributes = FileAttributes.Normal };
            // Set all files in the directory to Normal (i.e. not read only)
            foreach (var info in directory.GetFileSystemInfos("*", SearchOption.AllDirectories))
            {
                info.Attributes = FileAttributes.Normal;
            }
            // Then delete them
            directory.Delete(true);
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
        /// <param name="copySubDirs">Optionally copy all subdirectories</param>
        /// <param name="parameters">Configured ProgrammeParameters</param>
        public static void DirectoryCopyFunction(string sourceDirName, string destDirName, bool copySubDirs, ProgrammeParameters parameters)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);
            DirectoryInfo[] dirs = dir.GetDirectories();

            if (!dir.Exists)
            {
                AuxillaryFunctions.WriteLog(@"Source directory does not exist or could not be found " + sourceDirName, parameters.LocalLogFilename, -1, false, parameters);
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
                    DirectoryCopyFunction(subdir.FullName, temppath, copySubDirs, parameters);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="alignmentDir"></param>
        public static void PrepLRMRun(string alignmentDir)
        {
            Console.WriteLine("INFO: This run looks like it was created with LRM, and needs to be modified");

            // Create the correct Alignment folder
            string runFolder = Directory.GetParent(alignmentDir).FullName;
            string newFastqFolder = $@"{runFolder}\Data\Intensities\Basecalls";
            string newAlignmentFolder = $@"{newFastqFolder}\Alignment";
            Console.WriteLine($@"INFO: Creating a new alignment folder: {newAlignmentFolder}");
            Directory.CreateDirectory(newAlignmentFolder);

            // There should be a subfolder of the LRM Alignment folder with the date of analysis
            // This contains the other needed files (e.g. fastqs, SampleSheet)
            // Get this folder and then copy these files to the new alignment and fastq folders
            var subDirectories = Directory.GetDirectories(alignmentDir);
            string alignmentSubDir = subDirectories[0];
            string fastqFolder = $@"{alignmentSubDir}\Fastq";

            // Copy the FASTQs
            Console.WriteLine($@"INFO: Copying fastq files from {fastqFolder}");
            string pattern = @"\.fastq.gz";
            var files = Directory.GetFiles(fastqFolder).Where(x => Regex.IsMatch(x, pattern)).Select(x => x).ToList();
            foreach (var item in files)
            {
                // DEV: use copy instead of move for testing
                System.IO.File.Copy(item, newFastqFolder);
            };

            // Copy the remaining needed files
            //TODO
        }
    }
}
