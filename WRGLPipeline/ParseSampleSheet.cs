using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace WRGLPipeline
{
    struct SampleRecord
    {
        public string Sample_ID { get; private set; }
        public string Sample_Name { get; private set; }
        public string Analysis { get; private set; }
        public int Sample_No { get; private set; }
        /// <summary>
        /// Holds the details of a sample as read from the samplesheet
        /// </summary>
        /// <param name="sampleid">Sequencing ID of the sample</param>
        /// <param name="samplename">Name of the sample</param>
        /// <param name="analysis">Target analysis pipeline</param>
        /// <param name="samplenumber">Starlims ID of the sample</param>
        public SampleRecord(string sampleid, string samplename, string analysis, int samplenumber)
        {
            this.Sample_ID = sampleid;
            this.Sample_Name = samplename;
            this.Analysis = analysis;
            this.Sample_No = samplenumber;
        }
    }

    class ParseSampleSheet
    {
        public string ExperimentName { get; private set; }
        public string InvestigatorName { get; private set; }
        public List<SampleRecord> SampleRecords { get; private set; }
        public Dictionary<string, string> Analyses { get; private set; }

        readonly private ProgrammeParameters parameters;
        public ParseSampleSheet()
        {
            // Empty constructor so we can initialise, but then try/catch for any errors
        }

        /// <summary>
        /// Read an Illumina SampleSheet.csv to get sample IDs and names
        /// </summary>
        /// <param name="parameters">Configured ProgrammeParameters</param>
        public ParseSampleSheet( ProgrammeParameters parameters)
        {
            // If this isn't declared here (and above), then each function needs to have parameters passed to it.
            this.parameters = parameters;
            // If the samplesheet doesn't exist then raise an exception.
            if (!File.Exists(parameters.SampleSheetPath))
            {
                AuxillaryFunctions.WriteLog(parameters.SampleSheetPath + @" does not exist!", parameters.LocalLogFilename, -1, true, parameters);
                throw new FileNotFoundException();
            }

            // Populate fields
            // NOTE: "Investigator Name" is 'investigator name' in LRM samplesheet, but it's not really important
            this.SampleRecords = PopulateSampleSheetEntries();
            this.ExperimentName = GetSampleSheetField("Experiment Name");
            this.InvestigatorName = GetSampleSheetField("Investigator Name");

            // For Myeloid runs, the "Analysis" information is under the heading of "Manifests"
            // although it is otherwise the same. This situation should probably raise a more
            // informative error message if the field isn't present - it took a long time to figure it out.
            try
            {
                this.Analyses = GetSampleSheetField("Analysis");
            }
            catch
            {
                this.Analyses = GetSampleSheetField("Manifests");
            }
            
        }

        /// <summary>
        /// Reads each sample line and creates a SampleRecord each, which
        /// is added to a list.
        /// </summary>
        private List<SampleRecord> PopulateSampleSheetEntries()
        {
            List<SampleRecord> _sampleRecords = new List<SampleRecord>();
            string line;
            int n = 0;
            bool passedDataHeader = false;
            // Header : Column_Number to look up the required column
            Dictionary<string, int> ColumnHeaders = new Dictionary<string, int>(); 

            // Open the SampleSheet for reading
            // Separate FileStream and StreamReader usings are needed to ensure that we can open
            // files that are open elsewhere - this has caused problems in the past
            using (FileStream stream = new FileStream(parameters.SampleSheetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (StreamReader SampleSheet = new StreamReader(stream))
                {
                    //Continue to read until you reach end of file
                    while ((line = SampleSheet.ReadLine()) != null)
                    {
                        if (BlankLine(line))
                        {
                            continue;
                        }
                        // Skip lines before [Data] by matching the following regex pattern
                        // I think this avoid any problems with trailing commas that just a straight
                        // string match might encounter
                        Regex dataRgx = new Regex(@"Data");
                        if (dataRgx.IsMatch(line) && passedDataHeader == false)
                        {
                            passedDataHeader = true;
                            continue;
                        }

                        // On sample section
                        if (passedDataHeader == true)
                        {
                            // After the section header, the first row (n == 0) is the column header row
                            // So first those need to be read
                            if (n == 0)
                            {
                                // This gives and index for the foreach, which doesn't exist by default
                                // This is then stored as <column name>:<index> so that we can look up the index
                                // of a given column later. While not *strictly* needed, this does allow us to
                                // account for SampleSheets with different ordering or numbers of columns
                                // Lifted from https://stackoverflow.com/questions/521687/foreach-with-index
                                foreach (var field in line.Split(',').Select((x, i) => new { Value = x, Index = i }))
                                {
                                    ColumnHeaders.Add(field.Value, field.Index);
                                }
                                //continue;
                            }
                            else
                            {
                                //on sample info. split CSV fields
                                string [] fields = line.Split(',');

                                // Store the sample details in a SampleRecord
                                // NOTE: Analysis field is assigned using `condition ? consequent : alternative` operators
                                //       If there is an "Analysis" key present, assign that value or an empty string if not
                                // DEV: There must be more "correct" way of doing this, or at least better than having an empty string?
                                //      Maybe just use "null"?
                                SampleRecord tempRecord = new SampleRecord(sampleid: fields[ColumnHeaders[@"Sample_ID"]],
                                                                           samplename: fields[ColumnHeaders[@"Sample_Name"]],
                                                                           samplenumber: n,
                                                                           analysis: ColumnHeaders.ContainsKey(@"Analysis") ? fields[ColumnHeaders[@"Analysis"]] : "");
                                _sampleRecords.Add(tempRecord);
                            }                           
                            // Increment the sample counter - we can't do a for loop here as we don't know in advance
                            // how many samples there are. That could be a possible update but would require reading through
                            // the SampleSheet beforehand to find out. Which isn't exactly terrible given how long it is, but it
                            // *is* a little unecessary. Although it might make comprehension clearer here.
                            n++;
                        }
                    }
                }
            }
            return _sampleRecords;
        }

        /// <summary>
        /// Checks if a given line is empty or consists only of commas
        /// i.e. it is an empty csv line
        /// </summary>
        /// <param name="SampleSheetLine">Text line to check</param>
        /// <returns>true if the line is blank, false if any characters other than comma are found</returns>
        private static bool BlankLine(string SampleSheetLine)
        {
            // Check each character to see if there are
            // non-comma values. If so, line is NOT blank so return false.
            // There is no need to separately check a blank line, as that has
            // no characters, and so skips the foreach and returns true.
            foreach (char c in SampleSheetLine)
            {
                if (c != ',')
                {
                    return false;
                }
            }            
            return true;
        }

        /// <summary>
        /// Reads a specified header field from the SampleSheet
        /// This is called several times for different fields, which is obviously inefficient
        /// but the file size is so small that it really makes a negligable difference.
        /// </summary>
        /// <param name="field">Name of field to find</param>
        /// <returns>Value of the specified field from the SampleSheet</returns>
        private dynamic GetSampleSheetField(string field)
        {
            string line;
            dynamic temp;

            //Pass the file path and file name to the StreamReader constructor
            using (FileStream stream = new FileStream(parameters.SampleSheetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (StreamReader SampleSheet = new StreamReader(stream))
                {
                    //Continue to read until you reach end of file
                    while ((line = SampleSheet.ReadLine()) != null)
                    {
                        temp = GetSingleLineField(line, field);
                        if (temp != null) { return temp;  }
                        else
                        {
                            temp = GetAnalysisFields(line, field, SampleSheet);
                            if (temp != null) { return temp; }
                        }
                    }
                }
            }
            // If nothing matches, we will get to here so return the default "Unspecified" value
            return @"Unspecified";
        }
       
        /// <summary>
        /// Returns the value of the key:value pair matching field, if one exists 
        /// </summary>
        /// <param name="line">Text line to check for a match</param>
        /// <param name="field">Key string to look for</param>
        /// <returns>Value of key:value pair if field is found, or null if it is not</returns>
        private string GetSingleLineField(string line, string field)
        {
            // Add the string "^" to ensure that the regex matches only at the start of a line
            Regex FieldNameRgx = new Regex(@"^" + field);
            if (FieldNameRgx.IsMatch(line))
            {
                // Split the CSV line into a list -  this is <key>,<value> so
                // we want to return the second item
                string[] fields = line.Split(',');
                if (fields.Length > 0)
                {
                    return fields[1];
                }
            }
            return null;
        }
        
        /// <summary>
        /// Returns all values found under the [Analysis] (or other field) section in SampleSheet.csv
        /// As this is intended for the Analysis section only, it returns the first two values in each
        /// line, as a dictionary that would represent Panel:Reference
        /// </summary>
        /// <param name="line">Currrent line in SampleSheet</param>
        /// <param name="field">key [header] string to look for</param>
        /// <param name="SampleSheet">Open StreamReader of the target SampleSheet</param>
        /// <returns>List of values found under heading field, or null if nothing is found</returns>
        private Dictionary<string, string> GetAnalysisFields(string line, string field, StreamReader SampleSheet)
        {
            Dictionary<string, string> _analyses = new Dictionary<string, string>();
            // NOTE: the square brackets need to be escaped in a Regex!
            Regex HeadingNameRgx = new Regex(@"^\[" + field + @"\]");
            Regex NextHeadingRgz = new Regex(@"^\[");
            if (HeadingNameRgx.IsMatch(line))
            {
                // Heading found, read all following lines
                while ((line = SampleSheet.ReadLine()) != null)
                {
                    // Ignore blanks
                    if (!BlankLine(line))
                    {
                        // Break if we hit the next heading (e.g. a line starting with "[")
                        if (NextHeadingRgz.IsMatch(line))
                        {
                            break;
                        }
                        else
                        {
                            string[] fields = line.Split(',');
                            // Error encoutered in testing - tried to load multiple instances of the same panel
                            // This should never happen in real life, but just in case use a try/catch to deal with it.
                            try
                            {
                                _analyses.Add(fields[0], fields[1]);
                            }
                            catch (System.ArgumentException)
                            {
                                AuxillaryFunctions.WriteLog("Only one instance of a given panel may exist in a single SampleSheet", parameters.LocalLogFilename, -1, false, parameters);
                                throw;
                            }
                        }
                    }
                }
                return _analyses;
            }
            return null;
        }
    }
}
