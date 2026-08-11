USE cis440sum26team4;

CREATE TABLE IF NOT EXISTS anonymous_feedback (
    feedback_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    reference_number VARCHAR(11) NOT NULL,
    problem_header VARCHAR(120) NOT NULL,
    proposed_solution TEXT NOT NULL,
    category VARCHAR(50) NOT NULL,
    submitted_at_utc DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    upvote_count INT UNSIGNED NOT NULL DEFAULT 0,
    status VARCHAR(20) NOT NULL DEFAULT 'Pending',
    manager_comment VARCHAR(1000) NULL,

    PRIMARY KEY (feedback_id),
    UNIQUE KEY uq_feedback_reference (reference_number),

    CONSTRAINT chk_feedback_category CHECK (
        category IN (
            'Technology',
            'Tools',
            'Interpersonal',
            'Culture',
            'Benefits',
            'Salary'
        )
    ),

    CONSTRAINT chk_feedback_status CHECK (
    status IN (
    'Pending',
    'Accepted',
    'Denied'
    )
    ),

    CONSTRAINT chk_problem_length CHECK (
        CHAR_LENGTH(problem_header) BETWEEN 5 AND 120
    ),

    CONSTRAINT chk_solution_length CHECK (
        CHAR_LENGTH(proposed_solution) BETWEEN 5 AND 2000
    )
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS managers (
manager_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
username VARCHAR(50) NOT NULL,
password_hash CHAR(64) NOT NULL,

PRIMARY KEY (manager_id),
UNIQUE KEY uq_manager_username (username)
) ENGINE=InnoDB;

INSERT IGNORE INTO managers (username, password_hash)
VALUES ('manager', SHA2('password123', 256));

CREATE TABLE IF NOT EXISTS digest_subscribers (
subscriber_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
email VARCHAR(255) NOT NULL,
subscribed_at_utc DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,

PRIMARY KEY (subscriber_id),
UNIQUE KEY uq_digest_email (email)
) ENGINE=InnoDB;


CREATE TABLE IF NOT EXISTS digest_schedule (
    schedule_id INT NOT NULL PRIMARY KEY,
    last_sent_at_utc DATETIME NULL
);

INSERT IGNORE INTO digest_schedule (schedule_id, last_sent_at_utc)
VALUES (1, NULL);
