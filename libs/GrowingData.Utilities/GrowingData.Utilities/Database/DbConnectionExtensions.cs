﻿using System;
using System.Data.Common;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace GrowingData.Utilities {
	public static class DbConnectionExtensions {

		public static int DEFAULT_TIMEOUT = 0;


		/// <summary>
		/// Uses reflection to try to bind the results of a query to the 
		/// type provided
		/// </summary>
		/// <param name="cn"></param>
		/// <param name="sql"></param>
		/// <param name="ps"></param>
		/// <returns></returns>
		public static List<T> ExecuteAnonymousSql<T>(this DbConnection cn, string sql, object ps) where T : new() {
			using (var cmd = cn.CreateCommand()) {
				cmd.CommandText = sql;
				cmd.CommandTimeout = DEFAULT_TIMEOUT;
				if (ps != null) {
					foreach (var p in ps.GetType().GetProperties()) {
						cmd.Parameters.Add(GetParameter(cmd, "@" + p.Name, p.GetValue(ps)));
					}
				}

				var type = typeof(T);

				var properties = type.GetProperties().ToDictionary(x => x.Name);
				var fields = type.GetFields().ToDictionary(x => x.Name);

				using (var r = cmd.ExecuteReader()) {
					return ReflectResults<T>(r);
				}
			}
		}
		public static DbDataReader ExecuteSql<T>(this DbConnection cn, string sql, object ps) where T : new() {
			using (var cmd = cn.CreateCommand()) {
				cmd.CommandText = sql;
				cmd.CommandTimeout = DEFAULT_TIMEOUT;
				if (ps != null) {
					foreach (var p in ps.GetType().GetProperties()) {
						cmd.Parameters.Add(GetParameter(cmd, "@" + p.Name, p.GetValue(ps)));
					}
				}

				using (var r = cmd.ExecuteReader()) {
					return r;
				}
			}
		}

		public static List<T> ReflectResults<T>(DbDataReader r) where T : new() {

			var type = typeof(T);

			var properties = type.GetProperties().ToDictionary(x => x.Name);
			var fields = type.GetFields().ToDictionary(x => x.Name);

			HashSet<string> columnNames = null;
			List<T> results = new List<T>();
			while (r.Read()) {
				var obj = new T();

				if (columnNames == null) {
					columnNames = new HashSet<string>();
					for (var i = 0; i < r.FieldCount; i++) {
						columnNames.Add(r.GetName(i));
					}
				}

				foreach (var p in properties) {
					if (columnNames.Contains(p.Key)) {
						if (r[p.Key] != DBNull.Value) {
							p.Value.SetValue(obj, r[p.Key]);
						} else {
							if (p.Value.PropertyType.IsClass) {
								p.Value.SetValue(obj, null);
							}
						}
					}
				}
				foreach (var p in fields) {
					if (columnNames.Contains(p.Key)) {
						if (r[p.Key] != DBNull.Value) {
							p.Value.SetValue(obj, r[p.Key]);
						} else {
							if (p.Value.FieldType.IsClass) {
								p.Value.SetValue(obj, null);
							}

						}
					}
				}
				results.Add(obj);
			}

			return results;
		}



		/// <summary>
		/// Executes an SQL Command using the supplied connection and sql query.
		/// The object, "ps" will be reflected such that its properties are bound
		/// as named parameters to the query.
		/// </summary>
		/// <param name="cn"></param>
		/// <param name="sql"></param>
		/// <param name="ps"></param>
		/// <returns></returns>
		public static int ExecuteSql(this DbConnection cn, string sql, object ps) {
			using (var cmd = cn.CreateCommand()) {
				cmd.CommandText = sql;
				cmd.CommandTimeout = DEFAULT_TIMEOUT;
				if (ps != null) {
					foreach (var p in ps.GetType().GetProperties()) {
						cmd.Parameters.Add(GetParameter(cmd, "@" + p.Name, p.GetValue(ps)));
					}
				}
				return cmd.ExecuteNonQuery();
			}
		}


		/// <summary>
		/// Executes an SQL Command using the supplied connection and sql query.
		/// The object, "ps" will be reflected such that its properties are bound
		/// as named parameters to the query.
		/// </summary>
		/// <param name="cn"></param>
		/// <param name="sql"></param>
		/// <param name="ps"></param>
		/// <returns></returns>
		public static int ExecuteSql(this DbConnection cn, DbTransaction txn, string sql, object ps) {
			using (var cmd = cn.CreateCommand()) {
				cmd.CommandText = sql;
				cmd.CommandTimeout = DEFAULT_TIMEOUT;
				cmd.Transaction = txn;
				if (ps != null) {
					foreach (var p in ps.GetType().GetProperties()) {
						cmd.Parameters.Add(GetParameter(cmd, "@" + p.Name, p.GetValue(ps)));
					}
				}
				return cmd.ExecuteNonQuery();
			}
		}

		public static async Task<int> ExecuteSqlAsync(this DbConnection cn, string sql, object ps) {
			using (var cmd = cn.CreateCommand()) {
				cmd.CommandText = sql;
				cmd.CommandTimeout = DEFAULT_TIMEOUT;
				if (ps != null) {
					foreach (var p in ps.GetType().GetProperties()) {
						cmd.Parameters.Add(GetParameter(cmd, "@" + p.Name, p.GetValue(ps)));
					}
				}
				return await cmd.ExecuteNonQueryAsync();
			}
		}



		public static DbParameter GetParameter(DbCommand cmd, string name, object val) {
			DbParameter p = cmd.CreateParameter();
			p.ParameterName = name;
			p.Value = val == null ? (object)DBNull.Value : val;
			return p;
		}


		public static List<T> DumpList<T>(this DbConnection cn, string sql, object ps) {
			using (var cmd = cn.CreateCommand()) {
				cmd.CommandText = sql;
				cmd.CommandTimeout = DEFAULT_TIMEOUT;
				if (ps != null) {
					foreach (var p in ps.GetType().GetProperties()) {
						cmd.Parameters.Add(GetParameter(cmd, "@" + p.Name, p.GetValue(ps)));
					}
				}
				using (var reader = cmd.ExecuteReader()) {
					var list = new List<T>();
					while (reader.Read()) {
						if (reader[0] != DBNull.Value) {
							list.Add((T)reader[0]);
						}
					}
					return list;
				}
			}
		}


		public static DbDataReader DumpReader(this DbConnection cn, string sql, object ps) {
			using (var cmd = cn.CreateCommand()) {
				cmd.CommandText = sql;
				cmd.CommandTimeout = DEFAULT_TIMEOUT;
				if (ps != null) {
					foreach (var p in ps.GetType().GetProperties()) {
						cmd.Parameters.Add(GetParameter(cmd, "@" + p.Name, p.GetValue(ps)));
					}
				}
				return cmd.ExecuteReader();
			}
		}


		public static string DumpTSVFormatted(this DbConnection cn, string sql, object ps) {
			StringBuilder output = new StringBuilder();

			using (var cmd = cn.CreateCommand()) {
				cmd.CommandTimeout = DEFAULT_TIMEOUT;
				cmd.CommandText = sql;
				if (ps != null) {
					foreach (var p in ps.GetType().GetProperties()) {
						cmd.Parameters.Add(GetParameter(cmd, "@" + p.Name, p.GetValue(ps)));
					}
				}
				using (var reader = cmd.ExecuteReader()) {
					bool isFirst = true;
					int rowCount = 0;
					while (reader.Read()) {

						if (isFirst) {
							//MungLog.Log.LogEvent("MungedDataWriter.Write", "Retreiving...");
							// Recycle the same array so we're not constantly allocating

							List<string> names = new List<string>();

							for (var i = 0; i < reader.FieldCount; i++) {
								names.Add(reader.GetName(i));
							}
							var namesLine = string.Join("\t", names);
							string underline = new String('-', namesLine.Length + (names.Count * 3));

							output.AppendLine(underline);
							output.AppendLine(namesLine);
							output.AppendLine(underline);

							isFirst = false;
						}
						for (var i = 0; i < reader.FieldCount; i++) {
							output.AppendFormat("{0}\t", Serialize(reader[i]));
						}
						output.Append("\n");


						rowCount++;

					}
				}
			}


			return output.ToString();
		}

		public static string Serialize(object o) {
			if (o is DateTime) {
				return ((DateTime)o).ToString("yyyy-MM-dd HH':'mm':'ss");

			}
			if (o == DBNull.Value) {
				return "NULL";
			}

			if (o is string) {

				// Strings are escaped 
				return "\"" + Escape(o.ToString()) + "\"";

			}
			return o.ToString();

		}
		private static string Escape(string unescaped) {
			return unescaped
				.Replace("\\", "\\" + "\\")		// '\' -> '\\'
				.Replace("\"", "\\" + "\"");		// '"' -> '""'
		}


		public static string DumpJsonRows(this DbConnection cn, string sql, object ps) {
			using (var cmd = cn.CreateCommand()) {
				cmd.CommandTimeout = DEFAULT_TIMEOUT;
				cmd.CommandText = sql;
				cmd.CommandTimeout = 30;
				if (ps != null) {
					foreach (var p in ps.GetType().GetProperties()) {
						cmd.Parameters.Add(GetParameter(cmd, "@" + p.Name, p.GetValue(ps)));
					}
				}
				using (var reader = cmd.ExecuteReader()) {
					// Field names
					List<string> columnNames =
						Enumerable.Range(0, reader.FieldCount)
							.Select(x => reader.GetName(x))
							.ToList();
					List<Dictionary<string, string>> data = new List<Dictionary<string, string>>();
					while (reader.Read()) {
						Dictionary<string, string> rowData = new Dictionary<string, string>();
						for (var i = 0; i < reader.FieldCount; i++) {
							if (reader[i].GetType() == typeof(DateTime)) {
								// Use ISO time
								rowData[columnNames[i]] = ((DateTime)reader[i]).ToString("s");
							} else {
								rowData[columnNames[i]] = reader[i].ToString();
							}
						}
						data.Add(rowData);
					}
					return JsonConvert.SerializeObject(new { ColumnNames = columnNames, Rows = data });
				}
			}
		}


		public static DataTable DumpDataTable(this DbConnection cn, string sql, object ps) {
			using (var cmd = cn.CreateCommand()) {
				cmd.CommandTimeout = DEFAULT_TIMEOUT;
				cmd.CommandText = sql;
				if (ps != null) {
					foreach (var p in ps.GetType().GetProperties()) {
						cmd.Parameters.Add(GetParameter(cmd, "@" + p.Name, p.GetValue(ps)));
					}
				}
				using (var reader = cmd.ExecuteReader()) {
					// Field names
					var table = new DataTable();
					table.Load(reader);
					return table;
				}
			}
		}


	}
}
