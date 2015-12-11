using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using GrowingData.Utilities;
using NLog;
using NLog.Targets;
using System.Text;
using System.Text.RegularExpressions;

namespace GrowingData.Pipeliner {
	public abstract class Pipeline {


		private PipelineSettings _settings;
		private DirectoryInfo _pipelineDirectory;
		private string _pipelinePath;

		protected string _name;
		protected Logger _logger;

		public string WorkbookPath {
			get {
				return Path.Combine(_pipelineDirectory.Parent.Parent.FullName, "Workbooks");
			}
		}

		public string PipelinePath {
			get {
				return _pipelinePath;
			}
		}

		public PipelineSettings Settings { get { return _settings; } }


		public string Name { get { return _name; } }

		public abstract List<PipelineStep> Steps { get; }



		public Pipeline(string pipelinePath) {
			_pipelinePath = pipelinePath;
			_pipelineDirectory = new DirectoryInfo(_pipelinePath);
			_name = _pipelineDirectory.Name;
			_logger = LogManager.GetLogger(_name);
			Initialize();
		}

		public void Initialize() {
			var connectionsJsonPath = Path.Combine(PipelinePath, "settings.json");
			try {
				var json = File.ReadAllText(connectionsJsonPath);
				_settings = JsonConvert.DeserializeObject<PipelineSettings>(json);
			} catch (Exception ex) {
				_logger.Fatal("Unable to find settings file for Pipeline: {0}, expecting: {1}", _name, connectionsJsonPath);
			}
		}

		protected void StepComplete(int stepNumber) {
			_logger.Debug("Pipeline: {0}, Step: {1} complete", _name, stepNumber);
		}

		public bool RunStep(string name) {
			for (var i = 0; i < Steps.Count; i++) {
				var step = Steps[i];
				if (step.StepName == name) {
					_logger.Debug("Found {0} at Step #{1} in {2}.  Running...", name, i, _name);
					try {
						bool success = step.Step(step, i);
						return success;
					} catch (Exception ex) {
						_logger.Error(string.Format("Aborting {0} due to an Exception in {1} (Step: {2})\r\nMessage: {3}\r\n----------\r\n{4}", step.StepName, _name, i, ex.Message, ex.StackTrace));

					}
				}

			}
			_logger.Error("Unable to find {0} in {1}", name, _name);
			return false;
		}




		public bool RunAll() {
			for (var i = 0; i < Steps.Count; i++) {
				var step = Steps[i];
				_logger.Debug("Running {0} at Step #{1} in {2}...", step.StepName, i, _name);
				try {
					bool success = step.Step(step, i);
					if (!success) {
						_logger.Error(string.Format("Aborting {0} due to failure in {1} (Step: {2})", step.StepName, _name, i));
						break;
					}
				} catch (Exception ex) {
					_logger.Error(string.Format("Aborting {0} due to an Exception in {1} (Step: {2})\r\nMessage: {3}\r\n----------\r\n{4}", step.StepName, _name, i, ex.Message, ex.StackTrace));
				}
			}
			return false;
		}

		public PipelineStep ExecuteSqlFile(string connection, string filename) {
			var sqlPath = Path.Combine(PipelinePath, "Sql", filename);

			try {
				var stepName = Path.GetFileNameWithoutExtension(sqlPath);
				var sql = File.ReadAllText(sqlPath);

				return new PipelineStep() {
					StepName = stepName,
					Step = ExecuteSql(connection, sql)
				};
			} catch (Exception ex) {
				_logger.Fatal(ex, string.Format("ExecuteSqlFile failed: {0} \r\nFileName: {1}\r\n {2}", ex.Message, filename, ex.StackTrace));

				throw;
			}

		}

		public SqlConnection GetConnection(string connectionName) {
			var pipelineConnection = _settings.Connections.FirstOrDefault(x => x.Name == connectionName);
			return pipelineConnection.Connection;
		}



		public bool ExecuteSql(string connection, string sql, string stepName, int stepNumber) {
			var pipelineConnection = _settings.Connections.FirstOrDefault(x => x.Name == connection);

			try {
				using (var cn = pipelineConnection.Connection) {
					var lastRunPath = Path.Combine(PipelinePath, "LastRun");
					if (!Directory.Exists(lastRunPath)) {
						Directory.CreateDirectory(lastRunPath);
					}
					File.WriteAllText(Path.Combine(PipelinePath, "LastRun", stepName + ".sql"), sql);


					var batches = Regex.Split(sql, "\r\nGO\r\n");

					foreach (var batch in batches) {
						if (batch.Trim().Length == 0) {
							continue;
						}
						try {
							cn.ExecuteSql(batch, null);
						} catch (Exception x) {
							_logger.Fatal(x, string.Format(
@"ExecuteSql failed
Query:
-----------
{0}

Message:
-----------
{1}

Stack:		
-----------
{2}
", batch, x.Message, x.StackTrace));
							return false;
						}
					}
					StepComplete(stepNumber);

					return true;
				}


			} catch (Exception ex) {
				_logger.Fatal(ex, string.Format("ExecuteSql failed, {0} \r\n {1} \r\n {2}", ex.Message, sql, ex.StackTrace));
				return false;
			}

		}

		public Func<PipelineStep, int, bool> ExecuteSql(string connection, string sql) {
			return (step, stepNumber) => {
				return ExecuteSql(connection, sql, step.StepName, stepNumber);
			};


		}

	}
}
