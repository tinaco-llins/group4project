using System;
using System.Collections.Generic;
using System.Configuration;
using System.Runtime.Remoting.Messaging;
using System.Security.Cryptography.X509Certificates;
using System.Web.Services;
using MySql.Data.MySqlClient;
using System.Net;
using System.Net.Mail;
using System.Text;

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

        private static readonly string[] AllowedDayNames =
        {
            "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"
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


        public void SendDigestIfDue()
        {
            DateTime? lastSent = null;

            const string getScheduleSql = @"
        SELECT last_sent_at_utc
        FROM digest_schedule
        WHERE schedule_id = 1;";

            using (MySqlConnection connection =
                new MySqlConnection(GetConnectionString()))
            using (MySqlCommand command =
                new MySqlCommand(getScheduleSql, connection))
            {
                connection.Open();

                object result = command.ExecuteScalar();

                if (result != null && result != DBNull.Value)
                {
                    lastSent = Convert.ToDateTime(result);
                }
            }

            if (lastSent.HasValue &&
                lastSent.Value > DateTime.UtcNow.AddDays(-7))
            {
                return;
            }

            List<string> subscribers = new List<string>();

            const string subscriberSql = @"
        SELECT email
        FROM digest_subscribers;";

            using (MySqlConnection connection =
                new MySqlConnection(GetConnectionString()))
            using (MySqlCommand command =
                new MySqlCommand(subscriberSql, connection))
            {
                connection.Open();

                using (MySqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        subscribers.Add(reader.GetString("email"));
                    }
                }
            }

            if (subscribers.Count == 0)
            {
                return;
            }

            foreach (string email in subscribers)
            {
                DigestResponse response = SendWeeklyDigest(email);

                if (!response.Ok)
                {
                    return;
                }
            }

            const string updateSql = @"
        UPDATE digest_schedule
        SET last_sent_at_utc = UTC_TIMESTAMP()
        WHERE schedule_id = 1;";

            using (MySqlConnection connection =
                new MySqlConnection(GetConnectionString()))
            using (MySqlCommand command =
                new MySqlCommand(updateSql, connection))
            {
                connection.Open();
                command.ExecuteNonQuery();
            }
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

                using (MySqlCommand command = new MySqlCommand(sql, connection))
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
                                ProblemHeader = reader.GetString("problem_header"),
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

        [WebMethod(EnableSession = true)]
        public AnalyticsResponse GetFeedbackAnalytics()
        {
            if (Session["ManagerLoggedIn"] == null ||
                !(bool)Session["ManagerLoggedIn"])
            {
                return AnalyticsResponse.Failure(
                    "You must be logged in as a manager to view analytics.");
            }

            const string totalSql = @"
SELECT COUNT(*)
FROM anonymous_feedback;";

            const string categorySql = @"
SELECT category AS label, COUNT(*) AS item_count
FROM anonymous_feedback
GROUP BY category
ORDER BY item_count DESC, category ASC;";

            const string statusSql = @"
SELECT status AS label, COUNT(*) AS item_count
FROM anonymous_feedback
GROUP BY status
ORDER BY item_count DESC, status ASC;";

            const string topSql = @"
SELECT feedback_id, problem_header, category, status, upvote_count
FROM anonymous_feedback
ORDER BY upvote_count DESC, submitted_at_utc DESC
LIMIT @topLimit;";

            const string trendSql = @"
SELECT DATE(submitted_at_utc) AS submission_date,
       COUNT(*) AS item_count
FROM anonymous_feedback
GROUP BY DATE(submitted_at_utc)
ORDER BY submission_date ASC;";

            AnalyticsData analytics = new AnalyticsData
            {
                CategoryCounts = new List<AnalyticsCount>(),
                StatusCounts = new List<AnalyticsCount>(),
                MostUpvoted = new List<AnalyticsSuggestion>(),
                SubmissionTrend = new List<AnalyticsTrendPoint>()
            };

            try
            {
                using (MySqlConnection connection =
                    new MySqlConnection(GetConnectionString()))
                {
                    connection.Open();

                    using (MySqlCommand totalCommand =
                        new MySqlCommand(totalSql, connection))
                    {
                        analytics.TotalSuggestions = Convert.ToInt32(
                            totalCommand.ExecuteScalar());
                    }

                    using (MySqlCommand categoryCommand =
                        new MySqlCommand(categorySql, connection))
                    using (MySqlDataReader reader = categoryCommand.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            analytics.CategoryCounts.Add(new AnalyticsCount
                            {
                                Label = reader.GetString("label"),
                                Count = Convert.ToInt32(reader["item_count"])
                            });
                        }
                    }

                    using (MySqlCommand statusCommand =
                        new MySqlCommand(statusSql, connection))
                    using (MySqlDataReader reader = statusCommand.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            analytics.StatusCounts.Add(new AnalyticsCount
                            {
                                Label = reader.GetString("label"),
                                Count = Convert.ToInt32(reader["item_count"])
                            });
                        }
                    }

                    using (MySqlCommand topCommand =
                        new MySqlCommand(topSql, connection))
                    {
                        topCommand.Parameters.Add(
                            "@topLimit", MySqlDbType.Int32).Value = 5;

                        using (MySqlDataReader reader = topCommand.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                analytics.MostUpvoted.Add(new AnalyticsSuggestion
                                {
                                    Id = reader.GetInt64("feedback_id"),
                                    ProblemHeader = reader.GetString("problem_header"),
                                    Category = reader.GetString("category"),
                                    Status = reader.GetString("status"),
                                    UpvoteCount = reader.GetInt32("upvote_count")
                                });
                            }
                        }
                    }

                    using (MySqlCommand trendCommand =
                        new MySqlCommand(trendSql, connection))
                    using (MySqlDataReader reader = trendCommand.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            analytics.SubmissionTrend.Add(new AnalyticsTrendPoint
                            {
                                Date = reader.GetDateTime("submission_date")
                                    .ToString("yyyy-MM-dd"),
                                Count = Convert.ToInt32(reader["item_count"])
                            });
                        }
                    }
                }

                return AnalyticsResponse.Success(analytics);
            }
            catch (Exception)
            {
                return AnalyticsResponse.Failure(
                    "We could not load feedback analytics right now.");
            }
        }

        [WebMethod(EnableSession = true)]
        public PulseConfigResponse SavePulseQuestion(string questionText, int dayOfWeek, string sendTime)
        {
            if (Session["ManagerLoggedIn"] == null || !(bool)Session["ManagerLoggedIn"])
            {
                return PulseConfigResponse.Failure("You must be logged in as a manager to configure the pulse question.");
            }

            questionText = (questionText ?? String.Empty).Trim();
            if (questionText.Length < 5 || questionText.Length > 280)
            {
                return PulseConfigResponse.Failure("Question text must be between 5 and 280 characters.");
            }

            if (dayOfWeek < 0 || dayOfWeek > 6)
            {
                return PulseConfigResponse.Failure("Please choose a valid day of the week.");
            }

            TimeSpan parsedTime;
            if (!TimeSpan.TryParse(sendTime, out parsedTime))
            {
                return PulseConfigResponse.Failure("Please provide a valid send time (e.g. 09:00).");
            }

            string managerUsername = Session["ManagerUsername"] as string;

            try
            {
                using (MySqlConnection connection = new MySqlConnection(GetConnectionString()))
                {
                    connection.Open();
                    MySqlTransaction transaction = connection.BeginTransaction();

                    try
                    {
                        long managerId;
                        using (MySqlCommand lookupCommand = new MySqlCommand(
                            "SELECT manager_id FROM managers WHERE username = @username LIMIT 1;",
                            connection, transaction))
                        {
                            lookupCommand.Parameters.Add("@username", MySqlDbType.VarChar, 50).Value = managerUsername;
                            object result = lookupCommand.ExecuteScalar();
                            if (result == null)
                            {
                                transaction.Rollback();
                                return PulseConfigResponse.Failure("Could not identify the logged-in manager.");
                            }
                            managerId = Convert.ToInt64(result);
                        }

                        using (MySqlCommand deactivateCommand = new MySqlCommand(
                            "UPDATE pulse_questions SET is_active = 0 WHERE is_active = 1;",
                            connection, transaction))
                        {
                            deactivateCommand.ExecuteNonQuery();
                        }

                        int newQuestionId;
                        using (MySqlCommand insertCommand = new MySqlCommand(@" 
                            INSERT INTO pulse_questions (question_text, day_of_week, send_time, is_active, created_by)
                            VALUES (@questionText, @dayOfWeek, @sendTime, 1, @createdBy);
                            SELECT LAST_INSERT_ID();",
                            connection, transaction))
                        {
                            insertCommand.Parameters.Add("@questionText", MySqlDbType.VarChar, 280).Value = questionText;
                            insertCommand.Parameters.Add("@dayOfWeek", MySqlDbType.Byte).Value = dayOfWeek;
                            insertCommand.Parameters.Add("@sendTime", MySqlDbType.Time).Value = parsedTime;
                            insertCommand.Parameters.Add("@createdBy", MySqlDbType.Int64).Value = managerId;

                            newQuestionId = Convert.ToInt32(insertCommand.ExecuteScalar());
                        }

                        transaction.Commit();

                        return PulseConfigResponse.Success(new PulseQuestionConfig
                        {
                            QuestionId = newQuestionId,
                            QuestionText = questionText,
                            DayOfWeek = dayOfWeek,
                            DayName = AllowedDayNames[dayOfWeek],
                            SendTime = parsedTime.ToString(@"hh\:mm")
                        });
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
            catch (Exception)
            {
                return PulseConfigResponse.Failure("Could not save the pulse question right now. Please try again.");
            }
        }

        [WebMethod(EnableSession = true)]
        public PulseConfigResponse GetPulseQuestionConfig()
        {
            if (Session["ManagerLoggedIn"] == null || !(bool)Session["ManagerLoggedIn"])
            {
                return PulseConfigResponse.Failure("You must be logged in as a manager.");
            }

            const string sql = @"
             SELECT question_id, question_text, day_of_week, send_time
             FROM pulse_questions
             WHERE is_active = 1
             LIMIT 1;";

            try
            {
                using (MySqlConnection connection = new MySqlConnection(GetConnectionString()))
                using (MySqlCommand command = new MySqlCommand(sql, connection))
                {
                    connection.Open();
                    using (MySqlDataReader reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            return PulseConfigResponse.Success(null);
                        }

                        int dayOfWeek = reader.GetByte("day_of_week");
                        return PulseConfigResponse.Success(new PulseQuestionConfig
                        {
                            QuestionId = reader.GetInt32("question_id"),
                            QuestionText = reader.GetString("question_text"),
                            DayOfWeek = dayOfWeek,
                            DayName = AllowedDayNames[dayOfWeek],
                            SendTime = reader.GetTimeSpan("send_time").ToString(@"hh\:mm")
                        });
                    }
                }
            }
            catch (Exception)
            {
                return PulseConfigResponse.Failure("Could not load the current pulse question.");
            }
        }

        [WebMethod]
        public PulseConfigResponse GetCurrentPulseQuestion()
        {
            const string sql = @"
             SELECT question_id, question_text
             FROM pulse_questions
             WHERE is_active = 1
             LIMIT 1;";

            try
            {
                using (MySqlConnection connection = new MySqlConnection(GetConnectionString()))
                using (MySqlCommand command = new MySqlCommand(sql, connection))
                {
                    connection.Open();
                    using (MySqlDataReader reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            return PulseConfigResponse.Success(null);
                        }

                        return PulseConfigResponse.Success(new PulseQuestionConfig
                        {
                            QuestionId = reader.GetInt32("question_id"),
                            QuestionText = reader.GetString("question_text")
                        });
                    }
                }
            }
            catch (Exception)
            {
                return PulseConfigResponse.Failure("Could not load this week's pulse question.");
            }
        }

        [WebMethod]
        public PulseSubmitResponse SubmitPulseResponse(int questionId, string responseValue)
        {
            if (questionId <= 0)
            {
                return PulseSubmitResponse.Failure("Invalid question.");
            }

            responseValue = (responseValue ?? String.Empty).Trim();
            if (responseValue.Length == 0 || responseValue.Length > 500)
            {
                return PulseSubmitResponse.Failure("Please provide a response (up to 500 characters).");
            }

            const string sql = @"
             INSERT INTO pulse_responses (question_id, response_value)
             VALUES (@questionId, @responseValue);";

            try
            {
                using (MySqlConnection connection = new MySqlConnection(GetConnectionString()))
                using (MySqlCommand command = new MySqlCommand(sql, connection))
                {
                    command.Parameters.Add("@questionId", MySqlDbType.Int32).Value = questionId;
                    command.Parameters.Add("@responseValue", MySqlDbType.VarChar, 500).Value = responseValue;

                    connection.Open();
                    command.ExecuteNonQuery();
                }

                return PulseSubmitResponse.Success();
            }
            catch (Exception)
            {
                return PulseSubmitResponse.Failure("Could not record your response. Please try again.");
            }
        }
        [WebMethod]
        public DigestResponse GetWeeklyDigest()
        {
            const string sql = @"
        SELECT problem_header, proposed_solution, category, upvote_count
        FROM anonymous_feedback
        ORDER BY upvote_count DESC, submitted_at_utc DESC
        LIMIT 3;";

            StringBuilder digest = new StringBuilder();

            digest.AppendLine("Weekly Suggestion Digest");
            digest.AppendLine();
            digest.AppendLine("Here are this week's top suggestions:");
            digest.AppendLine();

            try
            {
                using (MySqlConnection connection =
                    new MySqlConnection(GetConnectionString()))
                using (MySqlCommand command =
                    new MySqlCommand(sql, connection))
                {
                    connection.Open();

                    using (MySqlDataReader reader = command.ExecuteReader())
                    {
                        int number = 1;

                        while (reader.Read())
                        {
                            digest.AppendLine(
                                number + ". " +
                                reader.GetString("problem_header"));

                            digest.AppendLine(
                                "Category: " +
                                reader.GetString("category"));

                            digest.AppendLine(
                                "Proposed solution: " +
                                reader.GetString("proposed_solution"));

                            digest.AppendLine(
                                "Upvotes: " +
                                reader.GetInt32("upvote_count"));

                            digest.AppendLine();

                            number++;
                        }

                        if (number == 1)
                        {
                            digest.AppendLine(
                                "There were no suggestions submitted this week.");
                        }
                    }
                }

                return DigestResponse.Success(digest.ToString());
            }
            catch (Exception)
            {
                return DigestResponse.Failure(
                    "The weekly digest could not be generated.");
            }
        }



        [WebMethod]
        public DigestResponse SendWeeklyDigest(string recipientEmail)
        {
            recipientEmail = (recipientEmail ?? String.Empty).Trim();

            if (String.IsNullOrWhiteSpace(recipientEmail))
            {
                return DigestResponse.Failure(
                    "Please provide an email address.");
            }

            DigestResponse digestResponse = GetWeeklyDigest();

            if (!digestResponse.Ok)
            {
                return DigestResponse.Failure(
                    "The weekly digest could not be generated.");
            }

            try
            {
                string smtpHost =
                    ConfigurationManager.AppSettings["SmtpHost"];

                int smtpPort =
                    Convert.ToInt32(
                        ConfigurationManager.AppSettings["SmtpPort"]);

                string smtpUsername =
                    ConfigurationManager.AppSettings["SmtpUsername"];

                string smtpPassword =
                    ConfigurationManager.AppSettings["SmtpPassword"];

                string fromEmail =
                    ConfigurationManager.AppSettings["DigestFromEmail"];

                using (MailMessage message = new MailMessage())
                {
                    message.From = new MailAddress(fromEmail);
                    message.To.Add(recipientEmail);

                    message.Subject = "Weekly Suggestion Digest";
                    message.Body = digestResponse.Digest;
                    message.IsBodyHtml = false;

                    using (SmtpClient client =
                        new SmtpClient(smtpHost, smtpPort))
                    {
                        client.EnableSsl = true;

                        client.Credentials =
                            new NetworkCredential(
                                smtpUsername,
                                smtpPassword);

                        client.Send(message);
                    }
                }

                return DigestResponse.Success(
                    "Weekly digest sent successfully.");
            }
            catch (Exception ex)
            {
                return DigestResponse.Failure(
                   ex.InnerException != null
                   ? ex.InnerException.Message
                   : ex.Message);
            }
        }

        [WebMethod]
        public DigestSubscriptionResponse SubscribeToDigest(string email)
        {
            email = (email ?? String.Empty).Trim();

            if (String.IsNullOrWhiteSpace(email) || !email.Contains("@"))
            {
                return DigestSubscriptionResponse.Failure(
                    "Please enter a valid email address.");
            }

            const string sql = @"
        INSERT IGNORE INTO digest_subscribers (email)
        VALUES (@email);";

            try
            {
                using (MySqlConnection connection =
                    new MySqlConnection(GetConnectionString()))
                using (MySqlCommand command =
                    new MySqlCommand(sql, connection))
                {
                    command.Parameters.Add(
                        "@email",
                        MySqlDbType.VarChar,
                        255
                    ).Value = email;

                    connection.Open();
                    command.ExecuteNonQuery();
                }

                return DigestSubscriptionResponse.Success();
            }
            catch (Exception)
            {
                return DigestSubscriptionResponse.Failure(
                    "Could not subscribe this email right now.");
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

    public class AnalyticsData
    {
        public int TotalSuggestions { get; set; }
        public List<AnalyticsCount> CategoryCounts { get; set; }
        public List<AnalyticsCount> StatusCounts { get; set; }
        public List<AnalyticsSuggestion> MostUpvoted { get; set; }
        public List<AnalyticsTrendPoint> SubmissionTrend { get; set; }
    }

    public class AnalyticsCount
    {
        public string Label { get; set; }
        public int Count { get; set; }
    }

    public class AnalyticsSuggestion
    {
        public long Id { get; set; }
        public string ProblemHeader { get; set; }
        public string Category { get; set; }
        public string Status { get; set; }
        public int UpvoteCount { get; set; }
    }

    public class AnalyticsTrendPoint
    {
        public string Date { get; set; }
        public int Count { get; set; }
    }

    public class AnalyticsResponse
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public AnalyticsData Analytics { get; set; }

        public static AnalyticsResponse Success(AnalyticsData analytics)
        {
            return new AnalyticsResponse
            {
                Ok = true,
                Message = null,
                Analytics = analytics
            };
        }

        public static AnalyticsResponse Failure(string message)
        {
            return new AnalyticsResponse
            {
                Ok = false,
                Message = message,
                Analytics = null
            };
        }
    }

public class PulseQuestionConfig
{
    public int QuestionId { get; set; }
    public string QuestionText { get; set; }
    public int DayOfWeek { get; set; }
    public string DayName { get; set; }
    public string SendTime { get; set; }
}

public class PulseConfigResponse
{
    public bool Ok { get; set; }
    public string Message { get; set; }
    public PulseQuestionConfig Question { get; set; }

    public static PulseConfigResponse Success(PulseQuestionConfig question)
    {
        return new PulseConfigResponse { Ok = true, Message = null, Question = question };
    }

    public static PulseConfigResponse Failure(string message)
    {
        return new PulseConfigResponse { Ok = false, Message = message, Question = null };
    }
}

public class PulseSubmitResponse
{
    public bool Ok { get; set; }
    public string Message { get; set; }

    public static PulseSubmitResponse Success()
    {
        return new PulseSubmitResponse { Ok = true, Message = "Thanks for your feedback!" };
    }

    public static PulseSubmitResponse Failure(string message)
    {
        return new PulseSubmitResponse { Ok = false, Message = message };
    }
}
public class DigestResponse
{
    public bool Ok { get; set; }
    public string Message { get; set; }
    public string Digest { get; set; }

    public static DigestResponse Success(string digest)
    {
        return new DigestResponse
        {
            Ok = true,
            Message = null,
            Digest = digest
        };
    }

    public static DigestResponse Failure(string message)
    {
        return new DigestResponse
        {
            Ok = false,
            Message = message,
            Digest = null
        };
    }
}
public class DigestSubscriptionResponse
{
    public bool Ok { get; set; }
    public string Message { get; set; }

    public static DigestSubscriptionResponse Success()
    {
        return new DigestSubscriptionResponse
        {
            Ok = true,
            Message = "You are subscribed to the weekly digest."
        };
    }

    public static DigestSubscriptionResponse Failure(string message)
    {
        return new DigestSubscriptionResponse
        {
            Ok = false,
            Message = message
        };
    }
}


