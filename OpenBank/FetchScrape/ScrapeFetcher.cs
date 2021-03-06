using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.IO;
using System.Security.Cryptography;
using System.Configuration;
using System.Diagnostics;
using System.Reflection;
using Newtonsoft.Json;
using NLog;

namespace OpenBank.FetchScrape
{
	public abstract class ScrapeFetcher
	{
		private const int MAX_WAIT_MILLISECONDS = 60000; //60 seconds

		private ScrapeFetchParameters m_parameters;
		private string m_assemblyPath;
		private string m_casperJsPath;
		private string m_scriptsBasePath;
		private string m_debugPath;
		private string m_workingID;
		private string m_debugWorkingPath;
		private string m_scriptPath;
		private string m_outputData;
		private string m_errorData;

		public ScrapeFetcher (ScrapeFetchParameters parameters)
		{
			m_parameters = parameters;

			m_assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			m_casperJsPath = Path.Combine(m_assemblyPath, "casperjs");
			m_scriptsBasePath = Path.Combine(m_assemblyPath, "casperjs/scripts");
			m_scriptPath = Path.Combine (m_scriptsBasePath, GetScriptName (m_parameters.FID));
			m_debugPath = Path.Combine(m_assemblyPath, "debug");
			int epocTime = ((int)(DateTime.UtcNow - new DateTime (1970, 1, 1)).TotalSeconds); 
			m_workingID = epocTime.ToString();
			m_debugWorkingPath = Path.Combine(m_debugPath, m_workingID);
		}

		public DTO.ServiceResponse Response { get; set; }

		protected abstract string GetScriptName (string fid);
		protected abstract void PrepScrape (Process process);
		protected abstract DTO.ServiceResponse ProcessScrape (string outputPath, string debugID);

		public bool Fetch()
		{
			List<string> missingParameters = ParameterRequired.GetMissingParameters (m_parameters); 
			if (missingParameters.Count > 0) {
				this.Response = new DTO.ResponseError (HttpStatusCode.BadRequest) {
					friendly_error = "The bank could not be contacted because of missing information.",
					detailed_error = string.Concat ("Missing parameter(s): ", string.Join (", ", missingParameters))
				};
			} else {
				try {
					Directory.CreateDirectory (m_debugWorkingPath);

					if (!File.Exists (m_scriptPath)) {
						throw new NotSupportedException (string.Format ("FID:{0} not supported", m_parameters.FID));
					}

					//Will execute command from /OpenBank/OpenBank/casperjs directory in format:
					//  phantomjs /home/bholt/OpenBank/OpenBank/casperjs/scripts/3000_accounts.js ~/dev/OpenBank/OpenBank/casperjs --user_id="bank_username"

					ProcessStartInfo startInfo = new ProcessStartInfo ();
					startInfo.FileName = ConfigurationManager.AppSettings ["phantomjs_path"];
					startInfo.UseShellExecute = false;
					startInfo.CreateNoWindow = true;
					startInfo.RedirectStandardOutput = true;
					startInfo.RedirectStandardError = true;
					startInfo.WorkingDirectory = m_casperJsPath;
					startInfo.Arguments = "--ssl-protocol=any";
					startInfo.Arguments += string.Format (" {0} {1}", m_scriptPath, m_casperJsPath);
					startInfo.Arguments += string.Format (" --user_id=\"{0}\"", m_parameters.UserID);
					startInfo.Arguments += string.Format (" --password=\"{0}\"", m_parameters.Password);
					startInfo.Arguments += string.Format (" --security_answers=\"{0}\"", m_parameters.SecurityAnswers);
					startInfo.Arguments += string.Format (" --output_path=\"{0}\"", m_debugWorkingPath);

					// Start the process with the info we specified.
					// Call WaitForExit and then the using statement will close.
					using (Process exeProcess = new Process()) {
						exeProcess.StartInfo = startInfo;
						exeProcess.OutputDataReceived += new DataReceivedEventHandler (exeProcess_OutputDataReceived);
						exeProcess.ErrorDataReceived += new DataReceivedEventHandler (exeProcess_ErrorDataReceived);

						PrepScrape (exeProcess);

						m_outputData += string.Format ("[START] {0} {1}{2}", startInfo.FileName, startInfo.Arguments, Environment.NewLine);
						exeProcess.Start ();

						exeProcess.BeginOutputReadLine ();
						exeProcess.BeginErrorReadLine ();
						exeProcess.WaitForExit (MAX_WAIT_MILLISECONDS);
					}

					if (File.Exists (Path.Combine (m_debugWorkingPath, "challenge_question.txt"))) {
						string challengeQuestion = File.ReadAllText (Path.Combine (m_debugWorkingPath, "challenge_question.txt"));
						this.Response = new DTO.ResponseError (HttpStatusCode.BadRequest) {
							friendly_error = string.Concat ("The following security question was asked: ", challengeQuestion),
							detailed_error = challengeQuestion,
							is_security_question_asked =  true
						};
					} else {
						this.Response = ProcessScrape (m_debugWorkingPath, m_workingID);
					}
				} catch (NotSupportedException nex){
					this.Response = new DTO.ResponseError (HttpStatusCode.BadRequest) {
						friendly_error = "Bank is not supported.",
						detailed_error = nex.Message
					};
				} catch (System.ComponentModel.Win32Exception) {
					this.Response = new DTO.ResponseError (HttpStatusCode.InternalServerError) {
						friendly_error = "An error occured when attempting to connect to the bank.",
						detailed_error = "executable could not be found"
					};
				} catch (Exception ex) {
					this.Response = new DTO.ResponseError (HttpStatusCode.InternalServerError) {
						friendly_error = "An error occured when processing the response from the bank.",
						detailed_error = ex.Message  
					};
				}
			}

			bool isError = (this.Response is DTO.ResponseError);
			if (isError || ConfigurationManager.AppSettings ["keep_scrape_debug_output"] == "true") {
				File.WriteAllText (Path.Combine (m_debugWorkingPath, "error.log"), m_errorData);
				File.WriteAllText (Path.Combine (m_debugWorkingPath, "output.log"), m_outputData);
			} else {
				Directory.Delete (m_debugWorkingPath, true);
			}

			return isError;
		}

		void exeProcess_ErrorDataReceived(object sender, DataReceivedEventArgs e)
		{
			m_errorData += (e.Data + Environment.NewLine);
		}

		void exeProcess_OutputDataReceived(object sender, DataReceivedEventArgs e)
		{
			m_outputData += (e.Data + Environment.NewLine);
		}
	}
}

