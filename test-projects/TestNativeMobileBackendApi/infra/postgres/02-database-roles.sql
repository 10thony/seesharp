-- PostgreSQL login roles and privileges (password authentication / SCRAM).
-- See: https://www.postgresql.org/docs/current/auth-methods.html
--
-- chatapp  : bootstrap superuser created by the Docker image (init scripts only).
-- chatapi  : least-privilege role used by the ASP.NET application at runtime.

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'chatapi') THEN
        CREATE ROLE chatapi
            LOGIN
            PASSWORD 'chatapi_password'
            NOSUPERUSER
            NOCREATEDB
            NOCREATEROLE
            NOREPLICATION;
    END IF;
END
$$;

GRANT CONNECT ON DATABASE chatdb TO chatapi;
GRANT USAGE ON SCHEMA public TO chatapi;
