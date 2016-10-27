using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;

namespace WRGLPipeline
{
    public struct SampleRecord
    {
        public string Sample_ID, Sample_Name, Analysis;
        public int Sample_No;
    }

    class ParseSampleSheet
    {
        string sampleSheetPath, localFastqDir, logFilename, experimentName, investigatorName;
        ProgrammeParameters parameters;
        List<SampleRecord> sampleRecords = new List<SampleRecord>();
        Dictionary<string, string> analyses = new Dictionary<string, string>();

        public ParseSampleSheet(string _sampleSheetPath, string _localFastqDir, string _logFilename, ProgrammeParameters _parameters)
        {
            this.sampleSheetPath = _sampleSheetPath;
            this.localFastqDir = _localFastqDir;
            this.logFilename = _logFilename;
            this.parameters = _parameters;

            if (!File.Exists(sampleSheetPath)) { AuxillaryFunctions.WriteLog(sampleSheetPath + @" does not exist!", logFilename, -1, true, parameters); throw new FileNotFoundException(); }

            //populate fields
            PopulateSampleSheetEntries();
            GetExperimentName();
            GetInvestigatorName();
            GetAnalyses();
        }

        private void PopulateSampleSheetEntries()
        {
            string line;
            int n = 0, j = 0;
            bool passedDataHeader = false;
            string[] fields;
            SampleRecord tempRecord;
            Dictionary<string, int> ColumnHeaders = new Dictionary<string, int>(); //Header:Column_Number
            Regex dataRgx = new Regex(@"Data");

            //Pass the file path and file name to the StreamReader constructor
            StreamReader SampleSheet = new StreamReader(sampleSheetPath);

            //Continue to read until you reach end of file
            while ((line = SampleSheet.ReadLine()) != null)
            {
                //skip empty and comma lines
                if (line == "" || CommaOnlyLine(line) == true)
                {
                    continue;
                }

                //skip lines before [Data]
                if (dataRgx.IsMatch(line) && passedDataHeader == false)
                {
                    passedDataHeader = true;
                    continue;
                }

                //on sample section
                if (passedDataHeader == true)
                {
                    //on Sample_ID header
                    if (n == 0)
                    {
                        n = 1; //passed Sample_ID header
                        fields = line.Split(',');

                        foreach (string field in fields){
                            ColumnHeaders.Add(field, j);
                            j++; //0-based index
                        }

                        continue;
                    }

                    //on sample info. split CSV fields
                    fields = line.Split(',');

                    tempRecord.Sample_ID = fields[ColumnHeaders[@"Sample_ID"]];
                    tempRecord.Sample_Name = fields[ColumnHeaders[@"Sample_Name"]];
                    tempRecord.Sample_No = n;

                    //get analysis type
                    if (ColumnHeaders.ContainsKey(@"Analysis"))
                    {
                        tempRecord.Analysis = fields[ColumnHeaders[@"Analysis"]];
                    }
                    else
                    {
                        tempRecord.Analysis = "";
                    }

                    //Add to list
                    sampleRecords.Add(tempRecord);

                    n++;
                }

            } //end reading samplesheet

            //close the file
            SampleSheet.Close();
        }

        private static bool CommaOnlyLine(string SampleSheetLine)
        {

            foreach (char c in SampleSheetLine){
                
                if (c != ',')
                {
                    return false;
                }

            }
            
            return true;
        }

        private void GetExperimentName()
        {
            string line;
            string[] fields;
            Regex experimentNameRgx = new Regex(@"^Experiment Name");

            //Pass the file path and file name to the StreamReader constructor
            StreamReader SampleSheet = new StreamReader(sampleSheetPath);

            //Continue to read until you reach end of file
            while ((line = SampleSheet.ReadLine()) != null)
            {
                //skip empty and comma lines
                if (line == "" || CommaOnlyLine(line) == true)
                {
                    continue;
                }

                if (experimentNameRgx.IsMatch(line))
                {
                    fields = line.Split(',');

                    if (fields.Length > 0)
                    {
                        experimentName = fields[1];
                    }
                    else
                    {
                        experimentName = @"Unspecified";
                    }

                    break;
                }

            } //end reading file
        }

        private void GetInvestigatorName()
        {
            string line;
            string[] fields;
            Regex investigatorNameRgx = new Regex(@"^Investigator Name");

            //Pass the file path and file name to the StreamReader constructor
            StreamReader SampleSheet = new StreamReader(sampleSheetPath);

            //Continue to read until you reach end of file
            while ((line = SampleSheet.ReadLine()) != null)
            {
                //skip empty and comma lines
                if (line == "" || CommaOnlyLine(line) == true)
                {
                    continue;
                }

                if (investigatorNameRgx.IsMatch(line))
                {
                    fields = line.Split(',');

                    if (fields.Length > 0)
                    {
                        investigatorName = fields[1];
                    }
                    else
                    {
                        investigatorName = @"Unspecified";
                    }

                    break;
                }

            } //end reading file
        }

        private void GetAnalyses()
        {
            bool passedManifestHeader = false;
            string line;
            string[] fields;
            Regex manifestRgx = new Regex(@"Analysis");

            //Pass the file path and file name to the StreamReader constructor
            StreamReader SampleSheet = new StreamReader(sampleSheetPath);

            //Continue to read until you reach end of file
            while ((line = SampleSheet.ReadLine()) != null)
            {
                //skip empty and comma lines
                if (line == "" || CommaOnlyLine(line) == true)
                {
                    continue;
                }

                if (manifestRgx.IsMatch(line) && passedManifestHeader == false)
                {
                    passedManifestHeader = true;
                    continue;
                }

                if (passedManifestHeader == true){

                    if (line.Substring(0, 1) == "["){
                        break;
                    }

                    fields = line.Split(',');

                    if (fields.Length > 1){
                        analyses.Add(fields[0], fields[1]); //analysis type P/G,<ROIfile>
                    }
                }

            } //end reading file
        }

        public string getInvestigatorName { get { return investigatorName; } }
        public string getExperimentName { get { return experimentName; } }
        public List<SampleRecord> getSampleRecords { get { return sampleRecords; } }
        public Dictionary<string, string> getAnalyses { get { return analyses; } }

    }
}
