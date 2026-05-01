-- Item Status Lookup Table
-- Creates a reference table for item status values

CREATE TABLE IF NOT EXISTS item_statuses (
    status_id SERIAL PRIMARY KEY,
    status_code VARCHAR(50) NOT NULL UNIQUE,
    status_name VARCHAR(100) NOT NULL,
    is_default BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Create index on status_code for faster lookups
CREATE INDEX IF NOT EXISTS idx_item_statuses_code ON item_statuses(status_code);

-- Insert standard item status values
INSERT INTO item_statuses (status_code, status_name, is_default)
VALUES 
    ('ACTIVE', 'Active', TRUE),
    ('INACTIVE', 'Inactive', FALSE),
    ('ARCHIVED', 'Archived', FALSE)
ON CONFLICT (status_code) DO NOTHING;