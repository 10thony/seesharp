-- Create labor category lookup table
CREATE TABLE IF NOT EXISTS labor_categories (
    id SERIAL PRIMARY KEY,
    category_name VARCHAR(50) NOT NULL UNIQUE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Insert labor category values
INSERT INTO labor_categories (category_name) VALUES
    ('Full Time'),
    ('Part Time'),
    ('Contractor')
ON CONFLICT (category_name) DO NOTHING;
