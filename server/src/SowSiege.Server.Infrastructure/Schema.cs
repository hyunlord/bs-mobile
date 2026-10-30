namespace SowSiege.Server.Infrastructure;

internal static class Schema
{
    internal const string ExtensionsSql = """
        CREATE TABLE IF NOT EXISTS account_domains(
          account_id uuid NOT NULL REFERENCES accounts(id), domain text NOT NULL,
          state_version integer NOT NULL, content_hash text NOT NULL, state_json jsonb NOT NULL,
          PRIMARY KEY(account_id,domain));
        CREATE TABLE IF NOT EXISTS account_job_clocks(
          account_id uuid PRIMARY KEY REFERENCES accounts(id), utc_highwater bigint NOT NULL);
        CREATE TABLE IF NOT EXISTS domain_events(
          account_id uuid NOT NULL REFERENCES accounts(id), version bigint NOT NULL,
          domain text NOT NULL, ordinal integer NOT NULL, kind text NOT NULL, data jsonb NOT NULL,
          PRIMARY KEY(account_id,version,domain,ordinal));
        INSERT INTO server_schema VALUES(2) ON CONFLICT DO NOTHING;
        """;

    internal const string Sql = """
        CREATE TABLE IF NOT EXISTS server_schema(version integer PRIMARY KEY);
        INSERT INTO server_schema VALUES (1) ON CONFLICT DO NOTHING;
        CREATE TABLE IF NOT EXISTS accounts(
          id uuid PRIMARY KEY, version bigint NOT NULL, content_hash text NOT NULL,
          config_version text NOT NULL, codec_version integer NOT NULL,
          payload bytea NOT NULL, utc_highwater bigint NOT NULL,
          logical_seconds bigint NOT NULL, fraction_ms integer NOT NULL);
        CREATE TABLE IF NOT EXISTS sessions(
          token_hash text PRIMARY KEY, account_id uuid NOT NULL REFERENCES accounts(id), expires_at timestamptz NOT NULL);
        CREATE TABLE IF NOT EXISTS commands(
          account_id uuid NOT NULL REFERENCES accounts(id), command_id uuid NOT NULL,
          payload_hash text NOT NULL, response text NOT NULL,
          PRIMARY KEY(account_id,command_id));
        CREATE TABLE IF NOT EXISTS resource_events(
          account_id uuid NOT NULL REFERENCES accounts(id), version bigint NOT NULL,
          material text NOT NULL, delta integer NOT NULL,
          PRIMARY KEY(account_id,version,material));
        CREATE TABLE IF NOT EXISTS outbox(
          account_id uuid NOT NULL REFERENCES accounts(id), version bigint NOT NULL,
          event_type text NOT NULL, PRIMARY KEY(account_id,version));
        """;
}
