USE cis440sum26team4;

CREATE TABLE IF NOT EXISTS anonymous_feedback (
    feedback_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    reference_number VARCHAR(11) NOT NULL,
    problem_header VARCHAR(120) NOT NULL,
    proposed_solution TEXT NOT NULL,
    category VARCHAR(50) NOT NULL,
    submitted_at_utc DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,

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

    CONSTRAINT chk_problem_length CHECK (
        CHAR_LENGTH(problem_header) BETWEEN 5 AND 120
    ),

    CONSTRAINT chk_solution_length CHECK (
        CHAR_LENGTH(proposed_solution) BETWEEN 5 AND 2000
    )
) ENGINE=InnoDB;