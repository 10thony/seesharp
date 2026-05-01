-- Items Table Creation Script for PostgreSQL
-- Creates an items table with supporting reference tables

-- Drop existing objects if they exist (for clean setup)
DROP TABLE IF EXISTS items CASCADE;
DROP TABLE IF EXISTS item_statuses CASCADE;
DROP TABLE IF EXISTS users CASCADE;

-- Users table (simplified for foreign key reference)
CREATE TABLE users (
    id SERIAL PRIMARY KEY,
    username VARCHAR(100) NOT NULL UNIQUE,
    email VARCHAR(255) NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Item statuses lookup table
CREATE TABLE item_statuses (
    id SERIAL PRIMARY KEY,
    status_name VARCHAR(50) NOT NULL UNIQUE,
    description TEXT
);

-- Insert default status values
INSERT INTO item_statuses (status_name, description) VALUES
('active', 'Item is currently in use'),
('inactive', 'Item is not currently in use'),
('maintenance', 'Item is under maintenance'),
('retired', 'Item has been retired');

-- Main items table
CREATE TABLE items (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    cost DECIMAL(10, 2),
    manufacturer VARCHAR(255),
    location VARCHAR(255),
    owner INTEGER REFERENCES users(id),
    status INTEGER REFERENCES item_statuses(id),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
