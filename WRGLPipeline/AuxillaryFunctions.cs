using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Mail;
using System.IO;

namespace WRGLPipeline
{
    class AuxillaryFunctions
    {
        /// <summary>
        /// Creates the network folder for backup of run files
        /// </summary>
        /// <param name="networkRootRunDir">Directory for network file backup</param>
        public static string MakeNetworkOutputDir(string networkRootRunDir)
        {
            // Define networkRootRunDir
            if (Directory.Exists(networkRootRunDir))
            {
                StringBuilder networkRootRunDirCompose = new StringBuilder(); //append current date and time to make folder unique

                networkRootRunDirCompose.Append(networkRootRunDir);
                networkRootRunDirCompose.Append(DateTime.Now.ToString(@"_dd-MM-yy_H-mm-ss"));

                networkRootRunDir = networkRootRunDirCompose.ToString();
            }

            // Create networkRootRunDir
            try
            {
                Directory.CreateDirectory(networkRootRunDir);
            }
            catch (Exception e)
            {
                Console.WriteLine(@"Could not create ouput directory: {0}", e.ToString());
                throw;
            }

            return networkRootRunDir;
        }

        /// <summary>
        /// Looks up the BED file region which contains the given variant
        /// </summary>
        /// <param name="gVariant">The variant to look up</param>
        /// <param name="BEDRecords">Parsed BED file</param>
        /// <returns>Name of the BED region containing the variant, or a blank string if no matches found</returns>
        public static string LookupAmpliconID(Tuple<string, int> gVariant, List<BEDRecord> BEDRecords) //give genomic coordinate return amplicon name
        {
            //iterate over records
            foreach (BEDRecord record in BEDRecords)
            {
                //check if base falls within region
                if (record.Chromosome == gVariant.Item1)
                {
                    //missing base belongs to this region
                    if (gVariant.Item2 >= record.Start && gVariant.Item2 <= record.End)
                    {
                        return record.Name;
                    }
                }
            }
            //return blank amplicon
            return "";
        }

        /// <summary>
        /// Log function - Writes to a log file and also displays message on the console
        /// </summary>
        /// <param name="logMessage">Message to be reported</param>
        /// /// <param name="parameters">Configured ProgrammeParameters</param>
        /// <param name="errorCode">Error code to prepend to message - 0: INFO, 1: WARNING, -1: ERROR</param>
        /// <param name="firstUse">If this is the first time the logger has been used it displays a different message</param>
        /// <remarks>
        /// TODO: Put firstUse into ProgrammeParameters or just into the class namespace and see if it can be remembered that way
        /// DEV: Just for neatness, could write a function to combine the logfile and Console writes - maybe a trainee exercise?
        /// </remarks>
        public static void WriteLog(string logMessage, ProgrammeParameters parameters, int errorCode = 0, bool firstUse = false)
        {
            using (StreamWriter logfile = File.AppendText(parameters.LocalLogFilename))
            {
                // If it's the first time the log has been written (for this running of the pipeline)
                // write a basic header
                if (firstUse == true)
                {
                    logfile.WriteLine(@"Starting WRGL Pipeline v" + parameters.PipelineManagerVersion);
                    Console.WriteLine(@"Starting WRGL Pipeline v" + parameters.PipelineManagerVersion);
                }

                logfile.Write("{0} {1} ", DateTime.Now.ToShortDateString(), DateTime.Now.ToLongTimeString());
                Console.Write("{0} {1} ", DateTime.Now.ToShortDateString(), DateTime.Now.ToLongTimeString());

                // We want to change the error level message depending on the errorCode
                switch (errorCode)
                {
                    case 0:
                        logfile.WriteLine("INFO: {0}", logMessage);
                        Console.WriteLine("INFO: {0}", logMessage);
                        break;
                    case 1:
                        logfile.WriteLine("WARN: {0}", logMessage);
                        Console.WriteLine("WARN: {0}", logMessage);
                        break;
                    case -1:
                        logfile.WriteLine("ERROR: {0}", logMessage);
                        Console.WriteLine("ERROR: {0}", logMessage);
                        break;
                }
            }
            // DEV: Emailing is not currently working
            // run failure email must be sent outside logging loop as it attaches
            // the log file. Within the loop this file handle is open and cannot be attached.
            //if (errorCode == -1)
            //{
            //send failed email to admin
            //SendRunFailEmail(parameters);
            //}
        }

        /// <summary>
        /// Works out the fastq-containing folder path from the Alignment folder
        /// </summary>
        /// <param name="SuppliedDir">String representing path to the Alignment folder</param>
        /// <returns>String representing path to the fastq containing folder</returns>
        public static string GetFastqDir(string SuppliedDir)
        {
            string[] folders = SuppliedDir.Split('\\');
            StringBuilder tempBuilder = new StringBuilder();

            //extract fastqDir
            for (int i = 0; i < folders.Length - 1; ++i)
            {
                tempBuilder.Append(folders[i] + '\\');
            }

            return tempBuilder.ToString();
        }

        /// <summary>
        /// Works out the Run ID from the path to the Alignment folder
        /// </summary>
        /// <param name="SuppliedDir">String representing path to the Alignment folder</param>
        /// <returns>Run/cartridge ID of the target run</returns>
        public static string GetRunID(string SuppliedDir)
        {
            string[] folders = SuppliedDir.Split('\\');
            return folders[folders.Length - 5];
        }

        /// <summary>
        /// Works out the root directory of the run from the path to the Alignment folder
        /// </summary>
        /// <param name="SuppliedDir">String representing path to the Alignment folder</param>
        /// <returns>String representing path to the root directory of the given run</returns>
        public static string GetRootRunDir(string SuppliedDir)
        {
            string[] folders = SuppliedDir.Split('\\');
            StringBuilder tempBuilder = new StringBuilder();

            //extract fastqDir
            for (int i = 0; i < folders.Length - 4; ++i)
            {
                tempBuilder.Append(folders[i] + '\\');
            }

            return tempBuilder.ToString();
        }

        /// <summary>
        /// Works out the analysis folder from the Alignment folder.
        /// Analysis folder is the folder *containing* the run folder
        /// (e.g. generally expected to be MiseqAnalysis)
        /// </summary>
        /// <param name="SuppliedDir">String representing path to the Alignment folder</param>
        /// <returns>String representing path to the local analysis folder</returns>
        public static string GetLocalAnalysisFolderDir(string SuppliedDir)
        {
            string[] folders = SuppliedDir.Split('\\');
            StringBuilder tempBuilder = new StringBuilder();

            // Extract local run dir
            for (int i = 0; i < folders.Length - 5; ++i)
            {
                tempBuilder.Append(folders[i] + '\\');
            }

            return tempBuilder.ToString();
        }

        /// <summary>
        /// Send an email to the admin email address if the run fails
        /// </summary>
        /// <param name="parameters">Configured ProgrammeParameters</param>
        /// <remarks>NOT CURRENTLY IN USE</remarks>
        public static void SendRunFailEmail(ProgrammeParameters parameters) //send admin email to notify of run failure
        {
            //compose email
            MailMessage mail = new MailMessage
            {
                Subject = @"Run failed analysis!",
                From = new MailAddress(parameters.AdminEmailAddress)
            };
            // DEV: this is failing as the file is open elsewhere
            //      ?? why isn't the file handle being close?
            //      ?? can we send it as a copy?
            //mail.Attachments.Add(new Attachment(logFilename));
            mail.Attachments.Add(new Attachment(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\WRGLPipeline.ini"));

            mail.To.Add(parameters.AdminEmailAddress);

            //configure mail
            SmtpClient smtp = new SmtpClient(@"send.nhs.net", 587)
            {
                EnableSsl = true
            };
            System.Net.NetworkCredential netCre = new System.Net.NetworkCredential(parameters.AdminEmailAddress, parameters.NHSMailPassword);
            smtp.Credentials = netCre;

            //send mail
            try
            {
                smtp.Send(mail);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not send email: {0}", ex.ToString());
            }
        }

        /// <summary>
        /// Send an email to all specified users on run completion
        /// </summary>
        /// <param name="logFilename"></param>
        /// <param name="variantReportFilePath"></param>
        /// <param name="sampleSheet"></param>
        /// <param name="pipelineID"></param>
        /// <param name="runID"></param>
        /// <param name="parameters"></param>
        /// <remarks>NOT CURRENTLY IN USE</remarks>
        public static void SendRunCompletionEmail(string logFilename, string variantReportFilePath, ParseSampleSheet sampleSheet, string pipelineID, string runID, ProgrammeParameters parameters)
        {
            StringBuilder html = new StringBuilder();

            html.Append("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">");
            html.Append("<html xmlns=\"http://www.w3.org/1999/xhtml\">");
            html.Append("<head>");
            html.AppendFormat("<title>{0}</title>", "WRGL Pipeline Notification");
            html.Append("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\" />");
            html.Append("</head>");
            html.Append("<body style=\"margin:0;padding:0;\" dir=\"ltr\">");
            html.Append("<table cellspacing=\"0\" cellpadding=\"0\" id=\"email_table\" style=\"border-collapse:collapse;width:98%;\" border=\"0\">");
            html.Append("<tr>");
            html.Append("<td id=\"email_content\" style=\"font-family:&#039;lucida grande&#039;,tahoma,verdana,arial,sans-serif;font-size:12px;\">");
            html.Append("<table cellspacing=\"0\" cellpadding=\"0\" width=\"620px\" style=\"border-collapse:collapse;width:620px;\" border=\"0\">");
            html.Append("<tr>");
            html.Append("<td style=\"font-size:11px;font-family:LucidaGrande,tahoma,verdana,arial,sans-serif;padding:0px;background-color:#f2f2f2;border-left:none;border-right:none;border-top:none;border-bottom:none;\">");
            html.Append("<table cellspacing=\"0\" cellpadding=\"0\" width=\"620px\" style=\"border-collapse:collapse;\">");
            html.Append("<tr>");
            html.Append("<td style=\"font-size:11px;font-family:LucidaGrande,tahoma,verdana,arial,sans-serif;padding:0px;width:620px;\">");
            html.Append("<table cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"border-collapse:collapse;width:100%;\">");
            html.Append("<tr>");
            html.Append("<td style=\"font-size:11px;font-family:LucidaGrande,tahoma,verdana,arial,sans-serif;padding:20px;background-color:#fff;border-left:none;border-right:none;border-top:none;border-bottom:none;\">");
            html.Append("<table cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;\">");
            html.Append("<tr>");
            html.Append("<td valign=\"top\" style=\"font-size:11px;font-family:LucidaGrande,tahoma,verdana,arial,sans-serif;padding-right:15px;text-align:left;\">");
            html.Append("<a href=\"http://www.wrgl.org.uk/Pages/home.aspx\" style=\"color:#3b5998;text-decoration:none;\">");
            html.Append("<img src=\"WRGLlogo250x282.jpg\" alt=\"\" height=\"141\" width=\"125\" style=\"border:0;\"/>");
            html.Append("</a>");
            html.Append("</td>");
            html.Append("<td valign=\"top\" style=\"font-size:11px;font-family:LucidaGrande,tahoma,verdana,arial,sans-serif;width:100%;text-align:left;\">");
            html.Append("<table cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;width:100%;\">");
            html.Append("<tr>");
            html.Append("<td style=\"font-size:11px;font-family:LucidaGrande,tahoma,verdana,arial,sans-serif;padding-bottom:10px;\">");
            html.Append("<table cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;width:100%;\">");
            html.Append("<tr>");
            html.Append("<td style=\"font-size:11px;font-family:LucidaGrande,tahoma,verdana,arial,sans-serif;padding-top:30px;\">");
            html.Append("<span style=\"color:#111111;font-size:14px;\">Wessex Regional Genetics Laboratory</span>");
            html.Append("</td>");
            html.Append("</tr>");
            html.Append("<tr>");
            html.Append("<td style=\"font-size:11px;font-family:LucidaGrande,tahoma,verdana,arial,sans-serif;padding-top:5px;\">");
            html.AppendFormat("<span style=\"color:#111111;font-size:14px;font-weight:bold;\">Analysis complete for {0}</span>", sampleSheet.ExperimentName);
            html.Append("</td>");
            html.Append("</tr>");
            html.Append("<tr>");
            html.Append("<td style=\"font-size:12px;font-family:LucidaGrande,tahoma,verdana,arial,sans-serif;padding-top:5px;\">");
            html.AppendFormat("<span style=\"color:#111111;\">Identifier: {0}</span>", runID);
            html.Append("</td>");
            html.Append("</tr>");
            html.Append("<tr>");
            html.Append("<td style=\"font-size:12px;font-family:LucidaGrande,tahoma,verdana,arial,sans-serif;padding-top:5px;\">");
            html.AppendFormat("<span style=\"color:#111111;\">Pipeline: {0}</span>", pipelineID);
            html.Append("</td>");
            html.Append("</tr>");
            html.Append("<tr>");
            html.Append("<td style=\"font-size:12px;font-family:LucidaGrande,tahoma,verdana,arial,sans-serif;padding-top:5px;\">");
            html.AppendFormat("<span style=\"color:#111111;\">Investigator Name: {0}</span>", sampleSheet.InvestigatorName);
            html.Append("</td>");
            html.Append("</tr>");
            html.Append("</table>");
            html.Append("</td>");
            html.Append("</tr>");
            html.Append("</table>");
            html.Append("</td>");
            html.Append("</tr>");
            html.Append("</table>");
            html.Append("</td>");
            html.Append("</tr>");
            html.Append("</table>");
            html.Append("</td>");
            html.Append("</tr>");
            html.Append("<tr>");
            html.Append("<td style=\"font-size:11px;font-family:LucidaGrande,tahoma,verdana,arial,sans-serif;padding:0px;width:620px;\">");
            html.Append("<table cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;width:100%;\" border=\"0\">");
            html.Append("<tr>");
            html.Append("<td style=\"font-size:11px;font-family:LucidaGrande,tahoma,verdana,arial,sans-serif;padding:7px 20px;background-color:#f2f2f2;border-left:none;border-right:none;border-top:1px solid #ccc;border-bottom:1px solid #ccc;\">");
            html.Append("<table cellspacing=\"0\" cellpadding=\"0\" style=\"\">");
            html.Append("<tr>");
            html.Append("<td style=\"font-size:11px;font-family:LucidaGrande,tahoma,verdana,arial,sans-serif;padding-left:0px;\">");
            html.Append("<table cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;\">");
            html.Append("<tr>");
            html.Append("<td style=\"border-width: 1px;border-style: solid;border-color: #29447E #29447E #1a356e;background-color: #5b74a8;\">");
            html.Append("<table cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;\">");
            html.Append("<tr>");
            html.Append("<td style=\"font-size:11px;font-family:LucidaGrande,tahoma,verdana,arial,sans-serif;padding:2px 6px 4px;border-top:1px solid #8a9cc2;\">");
            html.AppendFormat("<a href=\"{0}\" style=\"color:#3b5998;text-decoration:none;\">", variantReportFilePath);
            html.Append("<span style=\"font-weight:bold;white-space:nowrap;color: #ffffff;font-size: 13px;\">Variant Report</span>");
            html.Append("</a>");
            html.Append("</td>");
            html.Append("</tr>");
            html.Append("</table>");
            html.Append("</td>");
            html.Append("</tr>");
            html.Append("</table>");
            html.Append("</td>");
            html.Append("</tr>");
            html.Append("</table>");
            html.Append("</td>");
            html.Append("</tr>");
            html.Append("</table>");
            html.Append("</td>");
            html.Append("</tr>");
            html.Append("</table>");
            html.Append("</td>");
            html.Append("</tr>");
            html.Append("</table>");
            html.Append("<table cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"border-collapse:collapse;width:620px;\">");
            html.Append("<tr>");
            html.Append("<td style=\"font-size:11px;font-family:&#039;lucida grande&#039;, tahoma, verdana, arial, sans-serif;padding:30px 20px;background-color:#fff;border-left:none;border-right:none;border-top:none;border-bottom:none;color:#999999;border:none;\">");
            html.Append("<a>If you do not wish to receive these email notifications please contact your administrator.</a>");
            html.Append("<a>Wessex Regional Genetics Laboratory, Salisbury District Hospital, Wiltshire SP2 8BJ</a>");
            html.Append("<a>Telephone: 01722 429012 Fax: 01722 429009.</a>");
            html.Append("</td>");
            html.Append("</tr>");
            html.Append("</table>");
            html.Append("</td>");
            html.Append("</tr>");
            html.Append("</table>");
            html.Append("</body>");
            html.Append("</html>");

            // Compose email
            MailMessage mail = new MailMessage
            {
                Subject = sampleSheet.ExperimentName + @" analysis complete",
                Body = html.ToString(),
                IsBodyHtml = true,
                From = new MailAddress(parameters.AdminEmailAddress)
            };
            mail.Attachments.Add(new Attachment(parameters.WRGLLogoPath));

            // Add recipients
            foreach (string recipient in parameters.GetEmailRecipients)
            {
                mail.To.Add(recipient);
            }

            // Configure mail
            SmtpClient smtp = new SmtpClient(@"send.nhs.net", 587)
            {
                EnableSsl = true
            };
            System.Net.NetworkCredential netCre = new System.Net.NetworkCredential(parameters.AdminEmailAddress, ProgrammeParameters.ToInsecureString(parameters.NHSMailPassword));
            smtp.Credentials = netCre;

            // Send mail
            try
            {
                smtp.Send(mail);
            }
            catch (Exception ex)
            {
                AuxillaryFunctions.WriteLog($@"Could not send email: {ex.ToString()}", parameters, errorCode: -1);
            }

        }
    }
}
