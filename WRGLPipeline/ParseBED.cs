using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace WRGLPipeline
{
    public struct BEDRecord
    {
        public string chromosome;
        public UInt32 start;
        public UInt32 end;
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

        private void GetBEDRecords()
        {
            StreamReader BEDin = new StreamReader(BEDFilePath);
            BEDRecord tempBEDRecord;
            string line;

            while ((line = BEDin.ReadLine()) != null)
            {

                if (line != "") //skip empty lines
                {
                    string[] fields = line.Split('\t');

                    if (fields.Length < 4)
                    {
                        AuxillaryFunctions.WriteLog(@"BED file " + BEDFilePath + @" is malformed. Check file contains chromosome, start, end and name.", logFilename, -1, false, parameters);
                        throw new FileLoadException();
                    }
                    else
                    {
                        if (fields[0] == "" || fields[1] == "" || fields[2] == "" || fields[3] == "")
                        {
                            AuxillaryFunctions.WriteLog(@"BED file " + BEDFilePath + @" is malformed. Check file contains chromosome, start, end and name.", logFilename, -1, false, parameters);
                            throw new FileLoadException();
                        }

                        tempBEDRecord.chromosome = fields[0];
                        tempBEDRecord.start = Convert.ToUInt32(fields[1]);
                        tempBEDRecord.end = Convert.ToUInt32(fields[2]);
                        tempBEDRecord.name = fields[3];

                        BEDRecords.Add(tempBEDRecord);
                    }
                }

            }
        }

        public List<BEDRecord> getBEDRecords { get { return BEDRecords; } }
    }
}
