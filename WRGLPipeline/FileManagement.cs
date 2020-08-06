using System;
using System.Linq;
using System.Text;
using System.IO;
using System.Security.Cryptography;

namespace WRGLPipeline
{
    class FileManagement
    {
        public static void BackupFiles(string localFastqDir, string suppliedDir, string localRootRunDir, string networkRootRunDir, string logFilename, ProgrammeParameters parameters)
        {
            string[] FASTQfiles = Directory.GetFiles(localFastqDir, @"*.fastq.gz");

            //compute MD5 and copy FASTQ files and checksums to network location
            //fastqs (and md5s) should be in <runID>\Data\Intensities\BaseCalls
            
            //create BaseCalls folder
            Directory.CreateDirectory(networkRootRunDir + @"\Data\Intensities\BaseCalls");
            
            //loop over files and copy
            foreach (string FASTQfilePath in FASTQfiles){

                string FASTQFilename = Path.GetFileName(FASTQfilePath);
                string[] FASTQfilenameFields = FASTQFilename.Split('_');

                if (FASTQfilenameFields[0] != @"Undetermined") //skip unindexed data
                {
                    StreamWriter MD5File = new StreamWriter(FASTQfilePath + @".md5");

                    AuxillaryFunctions.WriteLog(@"Calculating MD5 checksum for " + FASTQFilename, logFilename, 0, false, parameters);

                    MD5File.Write(FileManagement.GetMD5HashFromFile(FASTQfilePath) + @"  " + FASTQFilename + "\n");
                    MD5File.Close();

                    AuxillaryFunctions.WriteLog(@"Copying files to network: " + FASTQFilename + ' ' + FASTQFilename + @".md5", logFilename, 0, false, parameters);

                    File.Copy(FASTQfilePath, networkRootRunDir + @"\Data\Intensities\BaseCalls\" + FASTQFilename);
                    File.Copy(FASTQfilePath + @".md5", networkRootRunDir + @"\Data\Intensities\BaseCalls\" + FASTQFilename + @".md5");
                }
            }

            //copy run files to network
            AuxillaryFunctions.WriteLog(@"Copying SampleSheet and performance metrics to network", logFilename, 0, false, parameters);

            Directory.CreateDirectory(networkRootRunDir + @"\Data\Intensities\BaseCalls\Alignment");
            File.Copy(suppliedDir + @"\SampleSheetUsed.csv", networkRootRunDir + @"\SampleSheetUsed.csv");
            File.Copy(suppliedDir + @"\SampleSheetUsed.csv", networkRootRunDir + @"\Data\Intensities\BaseCalls\Alignment\SampleSheetUsed.csv");
            File.Copy(suppliedDir + @"\DemultiplexSummaryF1L1.txt", networkRootRunDir + @"\Data\Intensities\BaseCalls\Alignment\DemultiplexSummaryF1L1.txt");

            // these go in root run folder, so this is ok
            File.Copy(localRootRunDir + @"runParameters.xml", networkRootRunDir + @"\runParameters.xml");
            File.Copy(localRootRunDir + @"RunInfo.xml", networkRootRunDir + @"\RunInfo.xml");

            //InterOp is correctly placed in the root run folder
            Directory.CreateDirectory(networkRootRunDir + @"\InterOp");
            FileManagement.DirectoryCopyFunction(localRootRunDir + @"InterOp", networkRootRunDir + @"\InterOp", false, logFilename, parameters);
        }

        public static void DeleteOldRuns(string logFilename, string localMiSeqAnalysisDir, ProgrammeParameters parameters)
        {
            //get dir list and sort by date creation
            var di = new DirectoryInfo(localMiSeqAnalysisDir);
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
                        AuxillaryFunctions.WriteLog(@"Deleting folder: " + localMiSeqAnalysisDir + directories[n], logFilename, 0, false, parameters);
                        // Use the custom ForceDeleteDirectory function to remove folders even if there are read only files present
                        ForceDeleteDirectory(localMiSeqAnalysisDir + directories[n]);
                    }
                    catch (Exception e)
                    {
                        AuxillaryFunctions.WriteLog(@"Could not delete folder: " + e.ToString(), logFilename, -1, false, parameters);
                    }
                }
            }

            //delete MiSeqOutput
            string[] outputSubfolders = Directory.GetDirectories(@"D:\Illumina\MiSeqOutput");
            foreach (string dir in outputSubfolders)
            {
                try
                {
                    AuxillaryFunctions.WriteLog(@"Deleting folder: " + dir, logFilename, 0, false, parameters);
                    ForceDeleteDirectory(dir);
                }
                catch (Exception e)
                {
                    AuxillaryFunctions.WriteLog(@"Could not delete folder: " + e.ToString(), logFilename, -1, false, parameters);
                }
            }

        }

        // <summary>
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

        public static void DirectoryCopyFunction(string sourceDirName, string destDirName, bool copySubDirs, string logFilename, ProgrammeParameters parameters)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);
            DirectoryInfo[] dirs = dir.GetDirectories();

            if (!dir.Exists)
            {
                AuxillaryFunctions.WriteLog(@"Source directory does not exist or could not be found " + sourceDirName, logFilename, -1, false, parameters);
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
                    DirectoryCopyFunction(subdir.FullName, temppath, copySubDirs, logFilename, parameters);
                }
            }
        }

    }
}
