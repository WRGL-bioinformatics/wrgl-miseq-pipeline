using System.Collections.Generic;
using System.IO;

namespace WRGLPipeline
{
    public struct BEDRecord
    {
        public string chromosome;
        public int start;
        public int end;
        public string name;
    }

    class ParseBED
    {
        private string BEDFilePath, logFilename;
        private ProgrammeParameters parameters;
        List<BEDRecord> BEDRecords = new List<BEDRecord>();

        public ParseBED(string _BEDFilePath, string _logFilename, ProgrammeParameters _parameters)
        {
            this.BEDFilePath = _BEDFilePath;
            this.logFilename = _logFilename;
            this.parameters = _parameters;

            GetBEDRecords();
        }

        /// <summary>
        /// Process the bed file
        /// </summary>
        /// <remarks>
        /// Read through the target BED file, processing each line to extract a BEDRecord with the
        /// relevant information.
        /// </remarks>
        private void GetBEDRecords()
        {
            // vars to process each line 
            BEDRecord tempBEDRecord;
            string line;

            // Open the BED file using a FileStream which *should* allow it to open files that are already opened in another program
            // and will also automatically close the connection once it's finished.
            using (FileStream stream = new FileStream(BEDFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (StreamReader BEDin = new StreamReader(stream))
                {
                    // Loop through each line in the given BED file
                    while ((line = BEDin.ReadLine()) != null)
                    {
                        // Only process if line is not blank. i.e. skip empty lines
                        if (line != "")
                        {
                            string[] fields = line.Split('\t');

                            // Error if the BED file appears to be malformed
                            if (fields.Length < 4 || fields[0] == "" || fields[1] == "" || fields[2] == "" || fields[3] == "")
                            {
                                AuxillaryFunctions.WriteLog(@"BED file " + BEDFilePath + @" is malformed. Check file contains chromosome, start, end and name.", logFilename, -1, false, parameters);
                                throw new FileLoadException();
                            }
                            else
                            {
                                // Split each line into its respective fields
                                // and store as a BEDRecord
                                // DEV: Can a struct have a constructor?
                                //      Then we could instantiate this with one line.
                                //      Possibly without needing to actually create the tempBEDREcord?
                                //      Structs don't need a "new" keyword to create
                                tempBEDRecord.chromosome = fields[0];
                                tempBEDRecord.start = int.Parse(fields[1]);
                                tempBEDRecord.end = int.Parse(fields[2]);
                                tempBEDRecord.name = fields[3];

                                BEDRecords.Add(tempBEDRecord);
                            }
                        }
                    }
                }
            }
        }

        // public property to get the list of all bed records
        public List<BEDRecord> getBEDRecords { get { return BEDRecords; } }
    }
}
