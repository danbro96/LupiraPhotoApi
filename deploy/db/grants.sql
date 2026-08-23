-- lupira-photo-api: provision the `lupira_photo` database on the shared medelynas-db.
-- One role, one logical database, isolated from the other Lupira apps (no cross-grants). The app owns the
-- `photo` schema (Marten, via `--apply-schema`) — no tables are created here.
--
-- Apply (TrueNAS Shell), substituting a freshly generated password:
--   LUPIRA_PHOTO_DB_PW="$(openssl rand -hex 32)"; echo "$LUPIRA_PHOTO_DB_PW"   # save to your password manager
--   docker exec -i medelynas-db psql -U medelynas_admin -v app_password="'$LUPIRA_PHOTO_DB_PW'" postgres < grants.sql

CREATE ROLE lupira_photo_user WITH LOGIN PASSWORD :'app_password';
CREATE DATABASE lupira_photo OWNER lupira_photo_user;
REVOKE ALL ON DATABASE lupira_photo FROM PUBLIC;
GRANT CONNECT ON DATABASE lupira_photo TO lupira_photo_user;
