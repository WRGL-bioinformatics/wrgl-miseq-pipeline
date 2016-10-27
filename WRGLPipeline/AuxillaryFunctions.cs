using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Mail;
using System.IO;

namespace WRGLPipeline
{
    class AuxillaryFunctions
    {
        public static string makeNetworkOutputDir(string networkRootRunDir)
        {
            //define networkRootRunDir
            if (Directory.Exists(networkRootRunDir))
            {
                StringBuilder networkRootRunDirCompose = new StringBuilder(); //append current date and time to make folder unique

                networkRootRunDirCompose.Append(networkRootRunDir);
                networkRootRunDirCompose.Append(DateTime.Now.ToString(@"_dd-MM-yy_H-mm-ss"));

                networkRootRunDir = networkRootRunDirCompose.ToString();
            }

            //create networkRootRunDir
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

        //return concatinated string for multiple amplicons
        public static string LookupAmpliconID(Tuple<string, uint> gVariant, List<BEDRecord> BEDRecords) //give genomic coordinate return amplicon name
        {
            //iterate over records
            foreach (BEDRecord record in BEDRecords)
            {
                //check if base falls within region
                if (record.chromosome == gVariant.Item1)
                {

                    if (gVariant.Item2 >= record.start && gVariant.Item2 <= record.end) //missing base belongs to this region
                    {
                        return record.name;
                    }

                }
            }

            return ""; //return blank amplicon
        }

        public static void WriteLog(string logMessage, string logFilename, int errorCode, bool firstUse, ProgrammeParameters parameters)
        {
            using (StreamWriter w = File.AppendText(logFilename))
            {
                if (firstUse == true)
                {
                    w.WriteLine(@"Starting WRGL Pipeline v" + Programme.WRGLversion);
                    Console.WriteLine(@"Starting WRGL Pipeline v" + Programme.WRGLversion);
                }

                w.Write("{0} {1} ", DateTime.Now.ToShortDateString(), DateTime.Now.ToLongTimeString());
                Console.Write("{0} {1} ", DateTime.Now.ToShortDateString(), DateTime.Now.ToLongTimeString());

                if (errorCode == 0)
                {
                    w.WriteLine("INFO: {0}", logMessage);
                    Console.WriteLine("INFO: {0}", logMessage);
                } else if (errorCode == 1){
                    w.WriteLine("WARN: {0}", logMessage);
                    Console.WriteLine("WARN: {0}", logMessage);
                }
                else if (errorCode == -1)
                {
                    w.WriteLine("ERROR: {0}", logMessage);
                    Console.WriteLine("ERROR: {0}", logMessage);
                }

                w.Close();
            }

            if (errorCode == -1){

                //send failed email to admin
                SendRunFailEmail(logFilename, parameters);
            }

        }

        public static string GetFastqDir(string arg)
        {
            string[] folders = arg.Split('\\');
            StringBuilder tempBuilder = new StringBuilder();

            //extract fastqDir
            for (int i = 0; i < folders.Length - 1; ++i)
            {
                tempBuilder.Append(folders[i] + '\\');
            }

            return tempBuilder.ToString();
        }

        public static string GetRunID(string arg)
        {
            string[] folders = arg.Split('\\');
            return folders[folders.Length - 5];
        }

        public static string GetRootRunDir(string arg)
        {
            string[] folders = arg.Split('\\');
            StringBuilder tempBuilder = new StringBuilder();

            //extract fastqDir
            for (int i = 0; i < folders.Length - 4; ++i)
            {
                tempBuilder.Append(folders[i] + '\\');
            }

            return tempBuilder.ToString();
        }

        public static string GetLocalAnalysisFolderDir(string arg)
        {
            string[] folders = arg.Split('\\');
            StringBuilder tempBuilder = new StringBuilder();

            //extract local run dir
            for (int i = 0; i < folders.Length - 5; ++i)
            {
                tempBuilder.Append(folders[i] + '\\');
            }

            return tempBuilder.ToString();
        }

        public static void SendRunFailEmail(string logFilename, ProgrammeParameters parameters) //send admin email to notify of run failure
        {
            //compose email
            MailMessage mail = new MailMessage();
            mail.Subject = @"Run failed analysis!";
            mail.From = new MailAddress(parameters.getAdminEmailAddress);
            mail.Attachments.Add(new Attachment(logFilename));
            mail.Attachments.Add(new Attachment(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\WRGLPipeline.ini"));

            mail.To.Add(parameters.getAdminEmailAddress);

            //configure mail
            SmtpClient smtp = new SmtpClient(@"send.nhs.net", 587);
            smtp.EnableSsl = true;
            System.Net.NetworkCredential netCre = new System.Net.NetworkCredential(parameters.getAdminEmailAddress, parameters.getNHSMailPassword);
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
            html.AppendFormat("<span style=\"color:#111111;font-size:14px;font-weight:bold;\">Analysis complete for {0}</span>", sampleSheet.getExperimentName);
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
            html.AppendFormat("<span style=\"color:#111111;\">Investigator Name: {0}</span>", sampleSheet.getInvestigatorName);
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

            //compose email
            MailMessage mail = new MailMessage();
            mail.Subject = sampleSheet.getExperimentName + @" analysis complete";
            mail.Body = html.ToString();
            mail.IsBodyHtml = true;
            mail.From = new MailAddress(parameters.getAdminEmailAddress);
            mail.Attachments.Add(new Attachment(parameters.getWRGLLogoPath));

            //add recipients
            foreach (string recipient in parameters.getEmailRecipients)
            {
                mail.To.Add(recipient);
            }

            //configure mail
            SmtpClient smtp = new SmtpClient(@"send.nhs.net", 587);
            smtp.EnableSsl = true;
            System.Net.NetworkCredential netCre = new System.Net.NetworkCredential(parameters.getAdminEmailAddress, ProgrammeParameters.ToInsecureString(parameters.getNHSMailPassword));
            smtp.Credentials = netCre;

            //send mail
            try
            {
                smtp.Send(mail);
            }
            catch (Exception ex)
            {
                AuxillaryFunctions.WriteLog(@"Could not send email: " + ex.ToString(), logFilename, -1, false, parameters);
            }

        }

    }
}
