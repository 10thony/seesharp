-- Create users table
CREATE TABLE IF NOT EXISTS users (
    id                BIGSERIAL PRIMARY KEY,
    name              VARCHAR(255) NOT NULL,
    role_id           INTEGER,
    labor_category_code VARCHAR(50)
);

-- Add index for common query patterns (optional but recommended)
CREATE INDEX IF NOT EXISTS idx_users_role_id ON users(role_id);
CREATE INDEX IF NOT EXISTS idx_users_labor_category_code ON users(labor_category_code);
