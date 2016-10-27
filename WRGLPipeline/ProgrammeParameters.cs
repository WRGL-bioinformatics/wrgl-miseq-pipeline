using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.IO;
using System.Security;
using System.Xml;

namespace WRGLPipeline
{
    class ProgrammeParameters
    {
        //[PanelAnalysis]
        private string coreBedFile; //'Core' regions that must be covered
        private string sotonUserName;
        private string SSHHostKey; //SSH fingerprint; ensures connection to expected server
        private string iridisHostKey; //SSH fingerprint; ensures connection to expected server
        private string panelScriptsDir;
        private string interpretationsFile;
        private string panelRepo;

        //[GenotypingAnalysis]
        private string b37FilePath;
        private string b37FastaPath;
        private string knownIndels1Path; //1000g
        private string knownIndels2Path; //Mils
        private string knownIndels3Path; //cosmic
        private string gatkKeyPath;
        private string genotypingRepo;

        //[ProgrammePaths]
        private string ampliconAlignerPath;
        private string javaPath;
        private string somaticVariantCallerPath;
        private string gatkPath;
        private string snpEffPath;
        private string samtoolsPath;

        //[CommonParameters]
        private string preferredTranscriptsFile; //ENST list of transcripts

        //[FileManagement]
        private bool deleteOldestLocalRun;

        //[Notifications]
        private string WRGLLogoPath;
        private string adminEmailAddress;
        private List<string> emailRecipients = new List<string>();

        //passwords
        private SecureString sotonPassWord = new SecureString();
        private SecureString NHSMailPassword = new SecureString();

        private Dictionary<string, string> parameters = new Dictionary<string, string>();
        private static byte[] entropy = System.Text.Encoding.Unicode.GetBytes(@"7ftw43hgh0u9hn6d:^77jg$chjch)");

        public ProgrammeParameters()
        {
            StreamReader inputFile = new StreamReader(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\WRGLPipeline.ini");
            string line;

            while ((line = inputFile.ReadLine()) != null)
            {
                if (line != @"" && line.Substring(0, 1) != @"[")
                {
                    string[] fields = line.Split('=');

                    if (fields.Length != 2)
                    {
                        throw new FileLoadException(@"Line: " + line + @" is malformed");
                    }

                    if (string.Compare(@"EmailRecipients", fields[0], true) == 0) //match
                    {
                        emailRecipients.Add(fields[1]);
                    }
                    else
                    {
                        parameters.Add(fields[0], fields[1]);
                    }

                }

            }

            inputFile.Close();

            //populate parameters
            coreBedFile = parameters[@"CoreFile"];
            sotonUserName = parameters[@"Username"];
            SSHHostKey = parameters[@"SSHHostKey"];
            iridisHostKey = parameters[@"IridisHostKey"];
            panelScriptsDir = parameters[@"PanelScriptsDir"];
            panelRepo = parameters[@"PanelRepository"];

            b37FilePath = parameters[@"b37Folder"];
            b37FastaPath = parameters[@"b37Fasta"];
            knownIndels1Path = parameters[@"knownIndels1VCF"];
            knownIndels2Path = parameters[@"knownIndels2VCF"];
            knownIndels3Path = parameters[@"knownIndels3VCF"];
            gatkKeyPath = parameters[@"GatkKey"];
            genotypingRepo = parameters[@"GenotypingRepository"];

            ampliconAlignerPath = parameters[@"AmpliconAligner"];
            javaPath = parameters[@"Java"];
            somaticVariantCallerPath = parameters[@"SomaticVariantCaller"];
            gatkPath = parameters[@"gatk"];
            snpEffPath = parameters[@"snpEff"];
            samtoolsPath = parameters[@"samtools"];

            preferredTranscriptsFile = parameters[@"PreferredTranscripts"];
            interpretationsFile = parameters[@"Interpretations"];

            deleteOldestLocalRun = Convert.ToBoolean(parameters[@"DeleteOldestLocalRun"]);

            WRGLLogoPath = parameters[@"WRGLLogoPath"];
            adminEmailAddress = parameters[@"AdminEmailAddress"];


            //check for installed password: soton
            if (File.Exists(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\soton.key"))
            {
                //read output to string
                using (StreamReader r = new StreamReader(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\soton.key"))
                {
                    sotonPassWord = DecryptString(r.ReadToEnd());// return securestring
                }
            }
            else
            {
                Console.WriteLine(@"Enter admin soton password");
                string encryptedData = EncryptString(GetPassword());

                using (StreamWriter w = new StreamWriter(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\soton.key")){
                    w.Write(encryptedData); //write encrypted password
                }

                sotonPassWord = DecryptString(encryptedData);
            }
            
            //check for installed password: nhsmail
            if (File.Exists(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\nhs.key"))
            {
                //read output to string
                using (StreamReader r = new StreamReader(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\nhs.key"))
                {
                    NHSMailPassword = DecryptString(r.ReadToEnd());// return securestring
                }
            }
            else
            {
                Console.WriteLine(@"Enter admin NHSMail password");
                string encryptedData = EncryptString(GetPassword());

                using (StreamWriter w = new StreamWriter(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\nhs.key"))
                {
                    w.Write(encryptedData); //write encrypted password
                }

                NHSMailPassword = DecryptString(encryptedData);
            }
        }

        private static SecureString GetPassword() //reads password from console and stores in secure string
        {
            SecureString pword = new SecureString();

            while (true)
            {
                ConsoleKeyInfo i = Console.ReadKey(true);
                if (i.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return pword;
                }
                else if (i.Key == ConsoleKey.Backspace)
                {
                    if (pword.Length > 0)
                    {
                        pword.RemoveAt(pword.Length - 1);
                        Console.Write("\b \b");
                    }
                }
                else
                {
                    pword.AppendChar(i.KeyChar);
                    Console.Write("*");
                }
            }
        }

        private static string EncryptString(System.Security.SecureString input)
        {
            byte[] encryptedData = System.Security.Cryptography.ProtectedData.Protect(
                System.Text.Encoding.Unicode.GetBytes(ToInsecureString(input)),
                entropy,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encryptedData);
        }

        private static SecureString DecryptString(string encryptedData)
        {
            try
            {
                byte[] decryptedData = System.Security.Cryptography.ProtectedData.Unprotect(
                    Convert.FromBase64String(encryptedData),
                    entropy,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return ToSecureString(System.Text.Encoding.Unicode.GetString(decryptedData));
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: {0}", ex.ToString());
                throw;
            }
        }

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

        public string getCoreBedFile { get { return coreBedFile; } }
        public string getSotonUserName { get { return sotonUserName; } }
        public string getSSHHostKey { get { return SSHHostKey; } }
        public string getIridisHostKey { get { return iridisHostKey; } }
        public string getPanelScriptsDir { get { return panelScriptsDir; } }
        public string getb37FilePath { get { return b37FilePath; } }
        public string getb37FastaPath { get { return b37FastaPath; } }
        public string getKnownIndels1Path { get { return knownIndels1Path; } }
        public string getKnownIndels2Path { get { return knownIndels2Path; } }
        public string getKnownIndels3Path { get { return knownIndels3Path; } }
        public string getGatkKeyPath { get { return gatkKeyPath; } }
        public string getAmpliconAlignerPath { get { return ampliconAlignerPath; } }
        public string getJavaPath { get { return javaPath; } }
        public string getSomaticVariantCallerPath { get { return somaticVariantCallerPath; } }
        public string getGatkPath { get { return gatkPath; } }
        public string getSnpEffPath { get { return snpEffPath; } }
        public string getSamtoolsPath { get { return samtoolsPath; } }
        public string getPreferredTranscriptsFile { get { return preferredTranscriptsFile; } }
        public string getInterpretationsFile { get { return interpretationsFile; } }
        public bool getDeleteOldestLocalRun { get { return deleteOldestLocalRun; } }
        public string getAdminEmailAddress { get { return adminEmailAddress; } }
        public string getWRGLLogoPath { get { return WRGLLogoPath; } }
        public List<string> getEmailRecipients { get { return emailRecipients; } }
        public SecureString getSotonPassWord { get { return sotonPassWord; } }
        public SecureString getNHSMailPassword { get { return NHSMailPassword; } }
        public string getPanelRepo { get { return panelRepo; } }
        public string getGenotypingRepo { get { return genotypingRepo; } }
    }
}
