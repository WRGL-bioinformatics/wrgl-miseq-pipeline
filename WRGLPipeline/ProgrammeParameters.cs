 using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security;
using Microsoft.Extensions.Configuration;

namespace WRGLPipeline
{
    /// <summary>
    /// Stores the parameters required for pipeline running.
    /// Loads in the settings from the WRGLPipeline.ini file, as well
    /// as checking for command line options.
    /// </summary>
    class ProgrammeParameters
    {
        // NOTE: the { get; private set; } syntax isn't *quite* as secure as using
        //       a private readonly variable and a public getter, but it's simpler and
        //       more than robust enough for our purposes.
        //       This method means we only need to add a single line to define the 
        //       variable and the getter, with another line to set the value in the
        //       constructor.
        //       Future C# versions are introducing a { get; init; } style, which will
        //       enforce the immutability more by *only* allowing the value to be set
        //       by a constructor.

        // [PanelAnalysis]
        public string CoreBedFile { get; private set; }
        public string SotonUserName { get; private set; }
        public string SSHHostKey { get; private set; }
        public string IridisHostKey { get; private set; }
        public string PanelScriptsDir { get; private set; }
        public string InterpretationsFile { get; private set; }
        public string PanelRepo { get; private set; }
        public bool GetData { get; private set; } = false; //if we want to run GetData only or a full UploadAndExecute analysis. Set false by default.
        public bool BamDownload { get; private set; } = true;

        // [GenotypingAnalysis]
        public string GenotypingReferenceFolderPath { get; private set; }
        public string GenotypingReferenceFastaPath { get; private set; }
        public string KnownIndels1Path { get; private set; } // 1000 Genomes indels file
        public string KnownIndels2Path { get; private set; } // Mills and Gold indels file
        public string KnownIndels3Path { get; private set; } // COSMIC indels file
        public string GatkKeyPath { get; private set; }
        public string GenotypingRepo { get; private set; } // Location to store genotyping results
        public int GenotypingMaxThreads { get; private set; }
        public int GeminiMultiThreads { get; private set; }

        // [MyeloidAnalysis]
        public string MyeloidCoverageTool { get; private set; }

        // [ProgrammePaths]
        public string AmpliconAlignerPath { get; private set; }
        public string JavaPath { get; private set; }
        public string SomaticVariantCallerPath { get; private set; }
        public string GatkPath { get; private set; }
        public string SnpEffPath { get; private set; }
        public string SamtoolsPath { get; private set; }
        public string GeminiPath { get; private set; }
        public string GeminiMultiPath { get; private set; }
        public string PiscesPath { get; private set; }

        //[CommonParameters]
        public string PreferredTranscriptsPath { get; private set; }
        public string PreferredTranscriptsFile { get; private set; }
        public string NetworkDirectory { get; private set; }
        public string BamStoreLocation { get; private set; }

        //[AnalysisParameters]
        public int GenotypingDepth { get; private set; }
        public int GenotypingQual { get; private set; }
        public int PanelsDepth { get; private set; }
        public int ExomeDepth { get; private set; }

        //[FileManagement]
        public bool DeleteOldestLocalRun { get; private set; }

        //[Notifications]
        readonly private List<string> EmailRecipients = new List<string>();
        public string WRGLLogoPath { get; private set; }
        public string AdminEmailAddress { get; private set; }
        public List<string> GetEmailRecipients { get { return EmailRecipients; } }

        //[Development]
        public bool CopyToNetwork { get; private set; } = true;
        public bool GenotypingUseGemini { get; private set; }
        public bool GenotypingUsePisces { get; private set; }

        // Passwords are (obviously!) not stored in the ini file
        public SecureString SotonPassword { get; private set; }
        public SecureString NHSMailPassword { get; private set; }

        // Added parameters that were previously defined in programme.cs and passed explicity to all other classes.
        // Makes more sense that they should be in here, so that only the parameters object needs to be passed.
        public string SuppliedDir { get; private set; }
        public string LocalFastqDir { get; private set; }
        public string SampleSheetPath { get; private set; }
        public string LocalRootRunDir { get; private set; }
        public string LocalMiSeqAnalysisDir { get; private set; }
        public string RunID { get; private set; }
        public string NetworkRootRunDir { get; set; }
        public string LocalLogFilename { get; private set; }

        public ProgrammeParameters()
        {
            // Empty constructor so we can initialise, but then try/catch for any errors
        }
        /// <summary>
        /// Read settings and parameters from .ini config file and command line arguments.
        /// Settings are the accessible through the properties of a ProgrammeParameters object.
        /// </summary>
        /// <param name="args">Cmd line arguments passed from the main function</param>
        public ProgrammeParameters(string[] args)
        {
            // Read the config ini using the Microsoft.Extension.Configuration.Ini nuget module

            // We need the path to the directory containing the current executable
            string exelocation = AppDomain.CurrentDomain.BaseDirectory;
            var config = new ConfigurationBuilder()
                .SetBasePath(exelocation)
                .AddIniFile("WRGLPipeline.ini", optional: false)
                .Build();

            // Populate parameters from ini (NOTE: access with <section>:<key> string)

            // [PanelAnalysis]
            CoreBedFile = config["PanelAnalysis:CoreFile"];
            SotonUserName = config["PanelAnalysis:Username"];
            SSHHostKey = config["PanelAnalysis:SSHHostKey"];
            IridisHostKey = config["PanelAnalysis:IridisHostKey"];
            PanelScriptsDir = config["PanelAnalysis:PanelScriptsDir"];
            PanelRepo = config["PanelAnalysis:PanelRepository"];

            // [GenotypingAnalysis]
            GenotypingReferenceFolderPath = config["GenotypingAnalysis:b37Folder"];
            GenotypingReferenceFastaPath = config["GenotypingAnalysis:b37Fasta"];
            KnownIndels1Path = config["GenotypingAnalysis:knownIndels1VCF"];
            KnownIndels2Path = config["GenotypingAnalysis:knownIndels2VCF"];
            KnownIndels3Path = config["GenotypingAnalysis:knownIndels3VCF"];
            GatkKeyPath = config["GenotypingAnalysis:GatkKey"];
            GenotypingRepo = config["GenotypingAnalysis:GenotypingRepository"];
            GenotypingMaxThreads = Int32.Parse(config["GenotypingAnalysis:GenotypingMaxThreads"]);
            GeminiMultiThreads = Int32.Parse(config["GenotypingAnalysis:GeminiMultiThreads"]);

            // [MyeloidAnalysis]
            MyeloidCoverageTool = config["MyeloidAnalysis:MyeloidCoverageTool"];

            // [ProgrammePaths]
            AmpliconAlignerPath = config["ProgrammePaths:AmpliconAligner"];
            JavaPath = config["ProgrammePaths:Java"];
            SomaticVariantCallerPath = config["ProgrammePaths:SomaticVariantCaller"];
            GatkPath = config["ProgrammePaths:Gatk"];
            SnpEffPath = config["ProgrammePaths:SnpEff"];
            SamtoolsPath = config["ProgrammePaths:Samtools"];
            GeminiPath = config["ProgrammePaths:Gemini"];
            GeminiMultiPath = config["ProgrammePaths:GeminiMulti"];
            PiscesPath = config["ProgrammePaths:Pisces"];

            // [CommonParameters]
            PreferredTranscriptsPath = config["CommonParameters:PreferredTranscripts"];
            PreferredTranscriptsFile = Path.GetFileName(PreferredTranscriptsPath);
            InterpretationsFile = config["CommonParameters:Interpretations"];
            NetworkDirectory = config["CommonParameters:NetworkDirectory"];
            BamStoreLocation = config["CommonParameters:BamStoreLocation"];

            // [AnalysisParameters]
            GenotypingDepth = Int32.Parse(config["AnalysisParameters:GenotypingDepth"]);
            GenotypingQual = Int32.Parse(config["AnalysisParameters:GenotypingQual"]);
            PanelsDepth = Int32.Parse(config["AnalysisParameters:PanelsDepth"]);
            ExomeDepth = Int32.Parse(config["AnalysisParameters:ExomeDepth"]);

            // [FileManagement]
            DeleteOldestLocalRun = Convert.ToBoolean(config["FileManagement:DeleteOldestLocalRun"]);

            // [Notifications]
            WRGLLogoPath = config["Notifications:WRGLLogoPath"];
            AdminEmailAddress = config["Notifications:AdminEmailAddress"];
            EmailRecipients = config["Notifications:EmailRecipients"].Split(',').ToList();

            // Development
            GenotypingUseGemini = Convert.ToBoolean(config["Development:GenotypingUseGemini"]);
            GenotypingUsePisces = Convert.ToBoolean(config["Development:GenotypingUsePisces"]);

            // Read the command line options (Options details are set in the ParseArgs class)
            var parser = new ParseArgs(args);
            SuppliedDir = parser.Path;
            GetData = parser.GetData;
            BamDownload = parser.BamDownload;
            CopyToNetwork = parser.CopyToNetwork;

            // Check for an underscore in the SuppliedDir
            // This indicates an LRM run, which can then be processed differently
            Console.WriteLine($@"DEV: SuppliedDir is {SuppliedDir}");
            Console.WriteLine($@"DEV: LRM check is looking at folder {new DirectoryInfo(SuppliedDir).Name}");
            if (new DirectoryInfo(SuppliedDir).FullName.Contains("Alignment_"))
            {
                SuppliedDir = FileManagement.PrepLRMRun(SuppliedDir);
            }

            // Load the remaining parameters derived from the suppliedDir argument
            LocalFastqDir = AuxillaryFunctions.GetFastqDir(SuppliedDir);
            SampleSheetPath = SuppliedDir + @"\SampleSheetUsed.csv";LocalRootRunDir = AuxillaryFunctions.GetRootRunDir(SuppliedDir);
            LocalMiSeqAnalysisDir = AuxillaryFunctions.GetLocalAnalysisFolderDir(SuppliedDir);
            RunID = AuxillaryFunctions.GetRunID(SuppliedDir);
            NetworkRootRunDir = NetworkDirectory + @"\" + RunID;
            LocalLogFilename = LocalRootRunDir + @"\WRGLPipeline.log";

            // Get the directory in which the executable file is present when run
            // i.e. the full path to the \bin folder
            string keyfolder = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\keys";

            // Load passwords from key files, or prompt from the user
            SotonPassword = ReadPasswordOrPrompt(keyfolder + @"\soton.key", "Enter Soton admin password:");
            NHSMailPassword = ReadPasswordOrPrompt(keyfolder + @"\nhs.key", "Enter NHS mail password:");
        }

        /// <summary>
        /// Reads a securestring password from a key file, or prompts the user to enter the password if missing.
        /// Saves user input as new key file in the specified location.
        /// </summary>
        /// <param name="keypath">Path to the expected key file</param>
        /// <param name="prompt">Prompt to display if file not found</param>
        /// <returns>SecureString password from file or user prompt.</returns>
        private static SecureString ReadPasswordOrPrompt(string keypath, string prompt)
        {
            SecureString Password = new SecureString();

            if (File.Exists(keypath))
            {
                //read output to string
                using (StreamReader r = new StreamReader(keypath))
                {
                    Password = DecryptString(r.ReadToEnd());
                }
            }
            else
            {
                Console.WriteLine(prompt);
                string encryptedData = EncryptString(GetPassword());

                using (StreamWriter w = new StreamWriter(keypath))
                {
                    w.Write(encryptedData); //write encrypted password
                }

                Password = DecryptString(encryptedData);
            }
            return Password;
        }

        /// <summary>
        /// Read a password entered by the user at the console and store as a SecureString
        /// DEV: There must be a more efficient way to get a password? Writing all this to handle
        ///      blanking out user input seems unecessary...
        /// </summary>
        /// <returns>Unencrypted SecureString of user password input from console</returns>
        private static SecureString GetPassword()
        {
            SecureString pword = new SecureString();

            while (true)
            {
                // Read user keypress
                ConsoleKeyInfo i = Console.ReadKey(true);
                // If "Enter" then return the entered string
                if (i.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return pword;
                }
                // This allows the user to delete previous characters
                else if (i.Key == ConsoleKey.Backspace)
                {
                    if (pword.Length > 0)
                    {
                        pword.RemoveAt(pword.Length - 1);
                        Console.Write("\b \b");
                    }
                }
                // For any other input, append this character to the SecureString.
                else
                {
                    pword.AppendChar(i.KeyChar);
                    Console.Write("*");
                }
            }
        }

        // DEV: While I'm sure this is good enough for our needs, there must be a more
        //      crypotgraphically secure way of doing this than literally hard-coding?
        /// <summary>
        /// Entropy variable used by the EncryptString and DecryptString functions
        /// </summary>
        readonly private static byte[] entropy = System.Text.Encoding.Unicode.GetBytes(@"7ftw43hgh0u9hn6d:^77jg$chjch)");

        /// <summary>
        /// Encrypt a SecureString
        /// </summary>
        /// <param name="input">String to be encrypted</param>
        /// <returns>Encrypted SecureString representation of input</returns>
        private static string EncryptString(System.Security.SecureString input)
        {
            byte[] encryptedData = System.Security.Cryptography.ProtectedData.Protect(
                System.Text.Encoding.Unicode.GetBytes(ToInsecureString(input)),
                entropy,
                System.Security.Cryptography.DataProtectionScope.LocalMachine);
            return Convert.ToBase64String(encryptedData);
        }

        /// <summary>
        /// Decrypt a SecureString
        /// </summary>
        /// <param name="input">Encrypted string to decrypt</param>
        /// <returns>Decrypted SecureString representation of input</returns>
        /// <remarks>
        /// It's not clear to me why Matt wrapped this in a try/catch, unless perhaps
        /// he'd encountered some kind of problems while testing? The key file should
        /// only be a valid encrypted SecureString.
        /// </remarks>
        private static SecureString DecryptString(string input)
        {
            // 
            try
            {
                byte[] decryptedData = System.Security.Cryptography.ProtectedData.Unprotect(
                    Convert.FromBase64String(input),
                    entropy,
                    System.Security.Cryptography.DataProtectionScope.LocalMachine);
                return ToSecureString(System.Text.Encoding.Unicode.GetString(decryptedData));
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: {0}", ex.ToString());
                throw;
            }
        }

        /// <summary>
        /// Converts a string to SecureString.
        /// </summary>
        /// <param name="input">String to convert</param>
        /// <returns>SecureString representation of input</returns>
        /// <remarks>
        /// Used by DecryptString when reading a key file, as the decryption
        /// of the base 64 key returns a string. Although this isn't re-used it
        /// is much clearer as a separate function.
        /// </remarks>
        private static SecureString ToSecureString(string input)
        {
            SecureString secure = new SecureString();
            foreach (char c in input)
            {
                secure.AppendChar(c);
            }
            secure.MakeReadOnly();

            return secure;
        }

        /// <summary>
        /// Converts a SecureString to a string
        /// </summary>
        /// <param name="input">SecureString to convert</param>
        /// <returns>String representation of input</returns>
        public static string ToInsecureString(SecureString input)
        {
            string returnValue = string.Empty;
            IntPtr ptr = System.Runtime.InteropServices.Marshal.SecureStringToBSTR(input);
            try
            {
                returnValue = System.Runtime.InteropServices.Marshal.PtrToStringBSTR(ptr);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.ZeroFreeBSTR(ptr);
            }
            return returnValue;
        }
    }
}
