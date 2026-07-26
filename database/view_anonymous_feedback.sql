USE cis440sum26team4;

SELECT
    reference_number,
    problem_header,
    proposed_solution,
    category,
    submitted_at_utc
FROM anonymous_feedback
ORDER BY submitted_at_utc DESC;