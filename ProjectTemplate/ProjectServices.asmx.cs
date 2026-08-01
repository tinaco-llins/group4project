using System;
using System.Collections.Generic;
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

    [WebMethod]
        public FeedbackFeedResponse GetFeedbackFeed()
        {
            //Return every submitted suggestion, newest first, for the employee-facing feed. No identifying info is returned since submissions are anonymous.
            const string sql = @"
SELECT feedback_id, reference_number, problem_header, proposed_solution, category, submitted_at_utc
FROM anonymous_feedback
Order BY submitted_at_utc DESC;";

            var items = new List<FeedbackItem>();

            try
            {
                using (MySqlConnection connection = new MySqlConnection(GetConnectionString()))
                
                using (MySqlCommand command = new MySqlCommand(sql,connection))
                {
                    connection.Open();

                    using (MySqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                            {
                            items.Add(new FeedbackItem
                                {
                                Id = reader.GetInt64("feedback_id"), 
                                ReferenceNumber = reader.GetString("reference_number"),
                                ProposedSolution = reader.GetString("proposed_solution"),
                                Category = reader.GetString("Category"),
                                SubmittedAt = reader.GetDateTime("submitted_at_utc")
                                    .ToString("yyyy-MM-dd HH:mm")
                            });
                        }
                    }
                }
                return FeedbackFeedResponse.Success(items);
            }
            catch (Exception)
            {
                return FeedbackFeedResponse.Failure(
                    "We could not load the feedback feed right now. Please try again.");
            }
        
        }

    [WebMethod(EnableSession = true)]
        public LoginResponse ManagerLogin(string username, string password)
        {
            username = (username ?? String.Empty).Trim();
            password = password ?? String.Empty;

            if (username.Length == 0 || password.Length == 0)
            {
                return LoginResponse.Failure(
                    "Please enter a username and password."
                    );
            }

            const string sql = @"
SELECT manager_id
FROM managers
WHERE username = @username
AND password_hash = SHA2(@password, 256)
LIMIT 1;";

            try
            {
                using (MySqlConnection connection =
                    new MySqlConnection(GetConnectionString()))
                using (MySqlCommand command =
                new MySqlCommand(sql, connection))
                {
                    command.Parameters.Add(
                        "@username",
                        MySqlDbType.VarChar,
                        50
                        ).Value = username;

                    command.Parameters.Add(
                        "@password",
                        MySqlDbType.VarChar,
                        255
                        ).Value = password;

                    connection.Open();
                    object result = command.ExecuteScalar();

                    if (result == null)
                    {
                        return LoginResponse.Failure(
                            "Invalid username or password."
                            );
                    }
                    Session["ManagerLoggedIn"] = true;
                    Session["ManagerUsername"] = username;

                    return LoginResponse.Success(username);
                }
            }
            catch (Exception)
            {
                return LoginResponse.Failure(
                    "We are unable to log you in. Please try again."
                    );
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

    public class FeedbackItem
    {
        public long Id { get; set; }
        public string ReferenceNumber { get; set; }
        public string ProblemHeader { get; set; }
        public string ProposedSolution { get; set; }
        public string Category { get; set; }
        public string SubmittedAt { get; set; }
    }

    public class FeedbackFeedResponse
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public List<FeedbackItem> Items { get; set; }

        public static FeedbackFeedResponse Success(List<FeedbackItem> items)
        {
            return new FeedbackFeedResponse {
                Ok = true,
                Message = null,
                Items = items };
        }

        public static FeedbackFeedResponse Failure(string message)
        {
            return new FeedbackFeedResponse
            {
                Ok = false,
                Message = message,
                Items = new List<FeedbackItem>()
            };
        }
    }

    public class LoginResponse
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public string Username { get; set; }

        public static LoginResponse Success(string username)
        {
            return new LoginResponse
            {
                Ok = true,
                Message = "Login successful.",
                Username = username
            };
        }
        public static LoginResponse Failure(string message)
        {
            return new LoginResponse
            {
                Ok = false,
                Message = message,
                Username = null
            };
        }
    }
    }