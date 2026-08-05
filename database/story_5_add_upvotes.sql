USE cis440sum26team4;

ALTER TABLE anonymous_feedback
ADD COLUMN upvote_count INT UNSIGNED NOT NULL DEFAULT 0;
