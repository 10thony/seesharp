-- Role Lookup Table
CREATE TABLE IF NOT EXISTS roles (
    role_id SERIAL PRIMARY KEY,
    role_name VARCHAR(50) NOT NULL UNIQUE
);

-- Insert default role values
INSERT INTO roles (role_name) VALUES 
    ('admin'),
    ('user')
ON CONFLICT (role_name) DO NOTHING;
