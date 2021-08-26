using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;

namespace WRGLPipeline
{
    struct BEDRecord
    {
        public string Chromosome { get; private set; }
        public int Start { get; private set; }
        public int End { get; private set; }
        public string Name { get; private set; }
        /// <summary>
        /// Represents a single BED region
        /// </summary>
        /// <param name="chromosome">Chromosome</param>
        /// <param name="start">Start position</param>
        /// <param name="end">End position</param>
        /// <param name="name">Region name/ID</param>
        /// /// <remarks>
        /// BED format:
        ///     <c>CHROM    START   END ID</c>
        ///     
        /// Can't really integrate checking into this struct without
        /// needing to make it a full Class, which isn't worth it 
        /// currently.
        /// </remarks>
        public BEDRecord(string chromosome, int start, int end, string name)
        {
            this.Chromosome = chromosome;
            this.Start = start;
            this.End = end;
            this.Name = name;
        }
    }

    class ParseBED
    {
        /// <summary>
        /// List of all regions in the BED file
        /// </summary>
        public List<BEDRecord> BEDRecords { get; private set; } = new List<BEDRecord>();

        readonly private string BEDFilePath;
        readonly private ProgrammeParameters parameters;
        
        /// <summary>
        /// Reads a given BED file into a list of BEDRecords representing each 
        /// region in the file.
        /// </summary>
        /// <param name="BEDFilePath"></param>
        /// <param name="parameters"></param>
        public ParseBED(string BEDFilePath, ProgrammeParameters parameters)
        {
            this.BEDFilePath = BEDFilePath;
            this.parameters = parameters;

            try
            {
                GetBEDRecords();
            }
            catch
            {
                // Log the error
                AuxillaryFunctions.WriteLog("Could not process BED file: " + this.BEDFilePath, parameters.LocalLogFilename, -1, false, parameters);
                // But we still want to end
                throw;
            }
        }

        /// <summary>
        /// Process the BED file
        /// </summary>
        /// <remarks>
        /// Read through the target BED file, processing each line to extract a BEDRecord with the
        /// relevant information.
        /// </remarks>
        private void GetBEDRecords()
        {
            // vars to process each line 
            string line;

            // Open the BED file using a FileStream which *should* allow it to open files that are already opened in another program
            // and will also automatically close the connection once it's finished.
            using (FileStream stream = new FileStream(this.BEDFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (StreamReader BEDin = new StreamReader(stream))
                {
                    // Loop through each line in the given BED file
                    while ((line = BEDin.ReadLine()) != null)
                    {
                        // Check that the current line is valid.
                        // Will raise errors if appropriate
                        if (ValidateBED(line))
                        {
                            string[] fields = line.Split('\t');
                            BEDRecord tempBEDRecord = new BEDRecord(chromosome: fields[0],
                                                                   start: int.Parse(fields[1]),
                                                                    end: int.Parse(fields[2]),
                                                                    name: fields[3]);
                            BEDRecords.Add(tempBEDRecord);
                            
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Checks if a BED field is valid, and if not writes the exact reason to the log.
        /// If the line is blank, it is ignored, otherwide throws FileLoadException
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        private bool ValidateBED(string line)
        {
            // Check for a blank line or a comment line
            if (line == "" || line.StartsWith(@"#") )
            {
                return false;
            }

            // split the line 
            string[] fields = line.Split('\t');

            // Check if there are (at least) the minimum number of required fields
            // DEV: If these both print the same message, then maybe wrap the checks in 
            //      a try/catch and re-raise in the catch. I want the tests to be separate,
            //      as that makes it easier to document them clearly.
            if ( fields.Length < 4)
            {
                AuxillaryFunctions.WriteLog(@"BED file " + BEDFilePath + @" is malformed. Not enough columns. Check file contains chromosome, start, end and name.", parameters.LocalLogFilename, -1, false, parameters);
                throw new FileLoadException();
            }
            // Can't have any blank fields
            if (fields[0] == "" || fields[1] == "" || fields[2] == "" || fields[3] == "")
            {
                AuxillaryFunctions.WriteLog(@"BED file " + BEDFilePath + @" is malformed. Contains blank fields. Check file contains chromosome, start, end and name.", parameters.LocalLogFilename, -1, false, parameters);
                throw new FileLoadException();
            }

            // Start and End positions must be integers
            // Discard the output value
            if ( ! int.TryParse(fields[1], out _) || ! int.TryParse(fields[2], out _) )
            {
                AuxillaryFunctions.WriteLog(@"BED file " + BEDFilePath + @" is malformed. Cannot parse position values to integer. Check file contains chromosome, start, end and name.", parameters.LocalLogFilename, -1, false, parameters);
                AuxillaryFunctions.WriteLog(@"Values: " + fields[1] + " and " + fields[2], parameters.LocalLogFilename, -1, false, parameters);
                throw new FileLoadException();
            }

            // No errors? Appears to be a valid line
            return true;
        }
    }
}
