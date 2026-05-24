-- Seed data for local development.
-- Demo password for seeded accounts: Password1!
-- Regenerate hashes: dotnet run --project infra/tools/GeneratePasswordHash -- "YourPassword"

INSERT INTO app_users (id, user_name, email, password_hash, display_name, role) VALUES
    (
        'a1111111-1111-1111-1111-111111111111',
        'demo',
        'demo@local.test',
        'AQAAAAIAAYagAAAAEG9Rie6MOBbGyKfYH2XL/FhDekijDy1CaLIiHanWXW0G0wDC6m4zHHe4Rq0/u5SoLA==',
        'Demo User',
        'User'
    ),
    (
        'b2222222-2222-2222-2222-222222222222',
        'admin',
        'admin@local.test',
        'AQAAAAIAAYagAAAAEG9Rie6MOBbGyKfYH2XL/FhDekijDy1CaLIiHanWXW0G0wDC6m4zHHe4Rq0/u5SoLA==',
        'Admin User',
        'Admin'
    )
ON CONFLICT (user_name) DO NOTHING;

-- Synthetic users for integration and end-to-end testing.
INSERT INTO app_users (id, user_name, email, password_hash, display_name, role) VALUES
    ('d1111111-1111-1111-1111-111111111111', 'synthetic01', 'synthetic01@local.test', 'AQAAAAIAAYagAAAAEG9Rie6MOBbGyKfYH2XL/FhDekijDy1CaLIiHanWXW0G0wDC6m4zHHe4Rq0/u5SoLA==', 'Synthetic User 01', 'User'),
    ('d2222222-2222-2222-2222-222222222222', 'synthetic02', 'synthetic02@local.test', 'AQAAAAIAAYagAAAAEG9Rie6MOBbGyKfYH2XL/FhDekijDy1CaLIiHanWXW0G0wDC6m4zHHe4Rq0/u5SoLA==', 'Synthetic User 02', 'User'),
    ('d3333333-3333-3333-3333-333333333333', 'synthetic03', 'synthetic03@local.test', 'AQAAAAIAAYagAAAAEG9Rie6MOBbGyKfYH2XL/FhDekijDy1CaLIiHanWXW0G0wDC6m4zHHe4Rq0/u5SoLA==', 'Synthetic User 03', 'User'),
    ('d4444444-4444-4444-4444-444444444444', 'synthetic04', 'synthetic04@local.test', 'AQAAAAIAAYagAAAAEG9Rie6MOBbGyKfYH2XL/FhDekijDy1CaLIiHanWXW0G0wDC6m4zHHe4Rq0/u5SoLA==', 'Synthetic User 04', 'User'),
    ('d5555555-5555-5555-5555-555555555555', 'synthetic05', 'synthetic05@local.test', 'AQAAAAIAAYagAAAAEG9Rie6MOBbGyKfYH2XL/FhDekijDy1CaLIiHanWXW0G0wDC6m4zHHe4Rq0/u5SoLA==', 'Synthetic User 05', 'User'),
    ('d6666666-6666-6666-6666-666666666666', 'synthetic06', 'synthetic06@local.test', 'AQAAAAIAAYagAAAAEG9Rie6MOBbGyKfYH2XL/FhDekijDy1CaLIiHanWXW0G0wDC6m4zHHe4Rq0/u5SoLA==', 'Synthetic User 06', 'User'),
    ('d7777777-7777-7777-7777-777777777777', 'synthetic07', 'synthetic07@local.test', 'AQAAAAIAAYagAAAAEG9Rie6MOBbGyKfYH2XL/FhDekijDy1CaLIiHanWXW0G0wDC6m4zHHe4Rq0/u5SoLA==', 'Synthetic User 07', 'User'),
    ('d8888888-8888-8888-8888-888888888888', 'synthetic08', 'synthetic08@local.test', 'AQAAAAIAAYagAAAAEG9Rie6MOBbGyKfYH2XL/FhDekijDy1CaLIiHanWXW0G0wDC6m4zHHe4Rq0/u5SoLA==', 'Synthetic User 08', 'User'),
    ('d9999999-9999-9999-9999-999999999999', 'synthetic09', 'synthetic09@local.test', 'AQAAAAIAAYagAAAAEG9Rie6MOBbGyKfYH2XL/FhDekijDy1CaLIiHanWXW0G0wDC6m4zHHe4Rq0/u5SoLA==', 'Synthetic User 09', 'User'),
    ('daaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'synthetic10', 'synthetic10@local.test', 'AQAAAAIAAYagAAAAEG9Rie6MOBbGyKfYH2XL/FhDekijDy1CaLIiHanWXW0G0wDC6m4zHHe4Rq0/u5SoLA==', 'Synthetic User 10', 'User')
ON CONFLICT (user_name) DO NOTHING;

INSERT INTO chat_messages (id, user_id, user_name, message, sent_at) VALUES
    (
        'c1111111-1111-1111-1111-111111111111',
        'a1111111-1111-1111-1111-111111111111',
        'demo',
        'Welcome to the chat!',
        NOW() - INTERVAL '2 hours'
    ),
    (
        'c2222222-2222-2222-2222-222222222222',
        'b2222222-2222-2222-2222-222222222222',
        'admin',
        'Server is ready for connections.',
        NOW() - INTERVAL '1 hour'
    )
ON CONFLICT (id) DO NOTHING;
