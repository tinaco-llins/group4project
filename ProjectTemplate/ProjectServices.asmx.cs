using System;
using System.Collections.Generic;
using System.Configuration;
using System.Security.Cryptography.X509Certificates;
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

        private static readonly string[] AllowedStatuses =
        {
            "Pending", "Accepted", "Denied"
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
SELECT feedback_id, reference_number, problem_header, proposed_solution, category, submitted_at_utc, upvote_count, status, manager_comment
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
                                UpvoteCount = reader.GetInt32("upvote_count"),
                                Status = reader.GetString("status"),
                                ManagerComment = reader.IsDBNull(reader.GetOrdinal("manager_comment"))
                                ? null
                                : reader.GetString("manager_comment"),
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

        [WebMethod]
        public UpvoteResponse UpvoteFeedback(long? feedbackId)
        {
            if (!feedbackId.HasValue || feedbackId.Value <= 0)
            {
                return UpvoteResponse.Failure("A valid feedback ID is required.");
            }

            const string updateSql = @"
UPDATE anonymous_feedback
SET upvote_count = upvote_count + 1
WHERE feedback_id = @feedbackId;";

            const string countSql = @"
SELECT upvote_count
FROM anonymous_feedback
WHERE feedback_id = @feedbackId;";

            try
            {
                using (MySqlConnection connection =
                    new MySqlConnection(GetConnectionString()))
                {
                    connection.Open();

                    using (MySqlCommand updateCommand =
                        new MySqlCommand(updateSql, connection))
                    {
                        updateCommand.Parameters.Add("@feedbackId",
                            MySqlDbType.Int64).Value = feedbackId.Value;

                        if (updateCommand.ExecuteNonQuery() == 0)
                        {
                            return UpvoteResponse.Failure(
                                "The selected feedback could not be found.");
                        }
                    }

                    using (MySqlCommand countCommand =
                        new MySqlCommand(countSql, connection))
                    {
                        countCommand.Parameters.Add("@feedbackId",
                            MySqlDbType.Int64).Value = feedbackId.Value;

                        int upvoteCount = Convert.ToInt32(
                            countCommand.ExecuteScalar());
                        return UpvoteResponse.Success(upvoteCount);
                    }
                }
            }
            catch (Exception)
            {
                return UpvoteResponse.Failure(
                    "We could not record your upvote right now. Please try again.");
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
            [WebMethod(EnableSession = true)]
            public StatusUpdateResponse UpdateFeedbackStatus(
                long? feedbackId,
                string status,
                string managerComment)
            {
                if (Session["ManagerLoggedIn"] == null ||
                    !(bool)Session["ManagerLoggedIn"])
                {
                    return StatusUpdateResponse.Failure(
                        "You must be logged in as a manager.");
                }

                if (!feedbackId.HasValue || feedbackId.Value <= 0)
                {
                    return StatusUpdateResponse.Failure(
                        "A valid feedback ID is required.");
                }

                status = (status ?? String.Empty).Trim();
                managerComment = (managerComment ?? String.Empty).Trim();

                if (Array.IndexOf(AllowedStatuses, status) < 0)
                {
                    return StatusUpdateResponse.Failure(
                        "Please select a valid status.");
                }

                if (managerComment.Length > 1000)
                {
                    return StatusUpdateResponse.Failure(
                        "The manager comment cannot exceed 1,000 characters.");
                }

                const string sql = @"
UPDATE anonymous_feedback
SET status = @status,
    manager_comment = @managerComment
WHERE feedback_id = @feedbackId;";

                try
                {
                    using (MySqlConnection connection =
                        new MySqlConnection(GetConnectionString()))
                    using (MySqlCommand command =
                        new MySqlCommand(sql, connection))
                    {
                        command.Parameters.Add(
                            "@status",
                            MySqlDbType.VarChar,
                            20
                        ).Value = status;

                        command.Parameters.Add(
                            "@managerComment",
                            MySqlDbType.VarChar,
                            1000
                        ).Value = String.IsNullOrWhiteSpace(managerComment)
                            ? (object)DBNull.Value
                            : managerComment;

                        command.Parameters.Add(
                            "@feedbackId",
                            MySqlDbType.Int64
                        ).Value = feedbackId.Value;

                        connection.Open();

                        if (command.ExecuteNonQuery() == 0)
                        {
                            return StatusUpdateResponse.Failure(
                                "The selected feedback could not be found.");
                        }
                    }

                    return StatusUpdateResponse.Success();
                }
                catch (Exception)
                {
                    return StatusUpdateResponse.Failure(
                        "The feedback status could not be updated right now.");
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

    public class FeedbackItem
    {
        public long Id { get; set; }
        public string ReferenceNumber { get; set; }
        public string ProblemHeader { get; set; }
        public string ProposedSolution { get; set; }
        public string Category { get; set; }
        public string Status { get; set; }
        public string ManagerComment { get; set; }
        public string SubmittedAt { get; set; }
        public int UpvoteCount { get; set; }
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

    public class UpvoteResponse
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public int UpvoteCount { get; set; }

        public static UpvoteResponse Success(int upvoteCount)
        {
            return new UpvoteResponse
            {
                Ok = true,
                Message = null,
                UpvoteCount = upvoteCount
            };
        }

        public static UpvoteResponse Failure(string message)
        {
            return new UpvoteResponse
            {
                Ok = false,
                Message = message,
                UpvoteCount = 0
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
    public class StatusUpdateResponse
    {
        public bool Ok { get; set; }
        public string Message { get; set; }

        public static StatusUpdateResponse Success()
        {
            return new StatusUpdateResponse
            {
                Ok = true,
                Message = "The feedback status was updated."
            };
        }

        public static StatusUpdateResponse Failure(string message)
        {
            return new StatusUpdateResponse
            {
                Ok = false,
                Message = message
            };
        }
    }


