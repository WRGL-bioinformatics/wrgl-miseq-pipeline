﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WRGLPipeline
{
    class MyeloidPipelineWrapper
    {
        // DEV: sampleSheet and fastqFileNames aren't really needed for this, but 
        //      I've included the so it matches the other pipeline wrappers.
        readonly private ParseSampleSheet sampleSheet;
        readonly private ProgrammeParameters parameters;
        /// <summary>
        /// Runs the myeloid coverage and trasfer Python app.
        /// </summary>
        /// <param name="_parameters">Configured ProgrammeParameters</param>
        /// <param name="_sampleSheet">Parsed SampleSheet</param>
        public MyeloidPipelineWrapper(ParseSampleSheet _sampleSheet, ProgrammeParameters _parameters)
        {
            this.sampleSheet = _sampleSheet;
            this.parameters = _parameters;

            ExecuteMyeloidPipeline();
        }
        /// <summary>
        /// Configures and runs the process for the myeloid app.
        /// </summary>
        public void ExecuteMyeloidPipeline()
        {
            AuxillaryFunctions.WriteLog($@"Starting myeloid post-run processing for {this.sampleSheet.ExperimentName}...", parameters.LocalLogFilename, 0, false, parameters);

            // The myeloid coverage script needs to get the Analysis folder for the run
            // this is passed to the pipeline by MiSeq Reporter as the only arg, but to match
            // the other pipeline wrappers it isn't passed directly to here. 
            // It should be in the parameters object, as SuppliedDir
            AuxillaryFunctions.WriteLog($@"Running myeloid coverage on {parameters.SuppliedDir}", parameters.LocalLogFilename, 0, false, parameters);

            // Create a logfile to record the tool output
            StreamWriter logFile = new StreamWriter(parameters.LocalRootRunDir + @"\MyeloidTransfer.log");
            Process myeloidTransfer = new Process();
            myeloidTransfer.StartInfo.FileName = parameters.MyeloidCoverageTool;
            myeloidTransfer.StartInfo.Arguments = parameters.SuppliedDir;
            myeloidTransfer.StartInfo.UseShellExecute = false;
            myeloidTransfer.StartInfo.RedirectStandardOutput = true;
            myeloidTransfer.StartInfo.RedirectStandardError = true;

            myeloidTransfer.Start();

            logFile.Write(myeloidTransfer.StandardOutput.ReadToEnd());
            logFile.Write(myeloidTransfer.StandardError.ReadToEnd());

            myeloidTransfer.WaitForExit();
            myeloidTransfer.Close();

            AuxillaryFunctions.WriteLog(@"Myeloid transfer complete.", parameters.LocalLogFilename, 0, false, parameters);
        }
    }
}