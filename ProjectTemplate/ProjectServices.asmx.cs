using System;
using System.Configuration;
using System.Web.Services;
using MySql.Data.MySqlClient;

namespace ProjectTemplate
{
    [WebService(Namespace = "http://tempuri.org/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [System.ComponentModel.ToolboxItem(false)]
    [System.Web.Script.Services.ScriptService]
    public class ProjectServices : WebService
    {
        private static readonly string[] AllowedCategories =
        {
            "Technology", "Tools", "Interpersonal", "Culture", "Benefits", "Salary"
        };

        private string GetConnectionString()
        {
            ConnectionStringSettings setting =
                ConfigurationManager.ConnectionStrings["WorkplaceFeedback"];

            if (setting == null || String.IsNullOrWhiteSpace(setting.ConnectionString))
            {
                throw new ConfigurationErrorsException(
                    "The WorkplaceFeedback connection string is not configured.");
            }

            return setting.ConnectionString;
        }

        [WebMethod]
        public FeedbackResponse SubmitFeedback(
            string problem_header,
            string proposed_solution,
            string category)
        {
            // Validate anonymous feedback before saving it.
            problem_header = (problem_header ?? String.Empty).Trim();
            proposed_solution = (proposed_solution ?? String.Empty).Trim();
            category = (category ?? String.Empty).Trim();

            if (Array.IndexOf(AllowedCategories, category) < 0)
            {
                return FeedbackResponse.Failure("Please select a valid category.");
            }

            if (problem_header.Length < 5 || problem_header.Length > 120)
            {
                return FeedbackResponse.Failure(
                    "Problem header must be between 5 and 120 characters.");
            }

            if (proposed_solution.Length < 5 || proposed_solution.Length > 2000)
            {
                return FeedbackResponse.Failure(
                    "Proposed solution must be between 5 and 2,000 characters.");
            }

            string referenceNumber = "FB-" +
                Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();

            // Use parameters to safely insert the feedback.
            const string sql = @"
                INSERT INTO anonymous_feedback
                    (reference_number, problem_header, proposed_solution, category)
                VALUES
                    (@referenceNumber, @problemHeader, @proposedSolution, @category);";

            try
            {
                using (MySqlConnection connection =
                    new MySqlConnection(GetConnectionString()))
                using (MySqlCommand command = new MySqlCommand(sql, connection))
                {
                    command.Parameters.Add("@referenceNumber",
                        MySqlDbType.VarChar, 11).Value = referenceNumber;
                    command.Parameters.Add("@problemHeader",
                        MySqlDbType.VarChar, 120).Value = problem_header;
                    command.Parameters.Add("@proposedSolution",
                        MySqlDbType.Text).Value = proposed_solution;
                    command.Parameters.Add("@category",
                        MySqlDbType.VarChar, 50).Value = category;

                    connection.Open();
                    command.ExecuteNonQuery();
                }

                return FeedbackResponse.Success(referenceNumber);
            }
            catch (Exception)
            {
                return FeedbackResponse.Failure(
                    "We could not save your feedback right now. Please try again.");
            }
        }
    }

    public class FeedbackResponse
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public string ReferenceNumber { get; set; }

        public static FeedbackResponse Success(string referenceNumber)
        {
            return new FeedbackResponse
            {
                Ok = true,
                Message = "Your feedback was submitted anonymously.",
                ReferenceNumber = referenceNumber
            };
        }

        public static FeedbackResponse Failure(string message)
        {
            return new FeedbackResponse
            {
                Ok = false,
                Message = message,
                ReferenceNumber = null
            };
        }
    }
}
