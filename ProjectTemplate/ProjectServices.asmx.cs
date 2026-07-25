using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Services;
using MySql.Data;
using MySql.Data.MySqlClient;
using System.Data;

namespace ProjectTemplate
{
	[WebService(Namespace = "http://tempuri.org/")]
	[WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
	[System.ComponentModel.ToolboxItem(false)]
	[System.Web.Script.Services.ScriptService]

	public class ProjectServices : System.Web.Services.WebService
	{
		////////////////////////////////////////////////////////////////////////
		///replace the values of these variables with your database credentials
		////////////////////////////////////////////////////////////////////////
		private string dbID = "cis440sum26team4";
		private string dbPass = "cis440sum26team4";
		private string dbName = "cis440sum26team4";
		////////////////////////////////////////////////////////////////////////

		////////////////////////////////////////////////////////////////////////
		///call this method anywhere that you need the connection string!
		////////////////////////////////////////////////////////////////////////
		private string getConString()
		{
			return "SERVER=107.180.1.16; PORT=3306; DATABASE=" + dbName + "; UID=" + dbID + "; PASSWORD=" + dbPass;
		}
		////////////////////////////////////////////////////////////////////////



		/////////////////////////////////////////////////////////////////////////
		//don't forget to include this decoration above each method that you want
		//to be exposed as a web service!
		[WebMethod(EnableSession = true)]
		/////////////////////////////////////////////////////////////////////////
		public string TestConnection()
		{
			try
			{
				string testQuery = "select * from test";

				////////////////////////////////////////////////////////////////////////
				///here's an example of using the getConString method!
				////////////////////////////////////////////////////////////////////////
				MySqlConnection con = new MySqlConnection(getConString());
				////////////////////////////////////////////////////////////////////////

				MySqlCommand cmd = new MySqlCommand(testQuery, con);
				MySqlDataAdapter adapter = new MySqlDataAdapter(cmd);
				DataTable table = new DataTable();
				adapter.Fill(table);
				return "Success!";
			}
			catch (Exception e)
			{
				return "Something went wrong, please check your credentials and db name and try again.  Error: " + e.Message;
			}
		}

		/////Story 4: As an employee, I'd like to be prompted to enter both a problem and suggested solution so that my feedback will be actionable.//////
		[WebMethod(EnableSession = true)]

		[System.Web.Script.Services.ScriptMethod(ResponseFormat = System.Web.Script.Services.ResponseFormat.Json)]

		public string SubmitFeedback(string employeeName, string problem, string suggestedSolution)
		{
			if (string.IsNullOrWhiteSpace(problem) || string.IsNullOrWhiteSpace(suggestedSolution))
			{
				return "{\"success\": false, \"message\": \"Both a problem and a suggested solution are required.\"}";
			}

			try
			{
				using (MySqlConnection con = new MySqlConnection(getConString()))
				{
					con.Open();

					string sql = "INSERT INTO actionable_feedback (employeeName, problem, suggestedSolution) " + "VALUES (@employeeName, @problem, @suggestedSolution)";

					using (MySqlCommand cmd = new MySqlCommand(sql, con))
					{
						object employeeNameValue;
						if (string.IsNullOrWhiteSpace(employeeName))
						{
							employeeNameValue = DBNull.Value;
						}
						else
						{
							employeeNameValue = employeeName.Trim();
						}
						cmd.Parameters.AddWithValue("@employeeName", employeeNameValue);

						cmd.Parameters.AddWithValue("@problem", problem.Trim());
						cmd.Parameters.AddWithValue("@suggestedSolution", suggestedSolution.Trim());
						cmd.ExecuteNonQuery();
					}
				}
				return "{\"success\": true, \"message\": \"Feedback saved.\"}";
			}
			catch (Exception e)
			{
				return "{\"success\": false, \"message\": \"" + e.Message.Replace("\"", "'") + "\"}";

			}
		}
	}
}


