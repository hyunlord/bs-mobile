namespace SowSiege.Server.Infrastructure;

public sealed partial class AuthStore
{
    // Version the extractor: changing an indexed function's meaning requires rebuilding its index.
    private const string CommandReferenceSql = """
        CREATE FUNCTION auth_command_response_ids_v1(response text) RETURNS text[]
        LANGUAGE plpgsql IMMUTABLE STRICT PARALLEL SAFE
        SET search_path = pg_catalog
        AS $function$
        DECLARE
          ids text[] := ARRAY[]::text[];
          search_from integer := 1;
          hit integer;
        BEGIN
          LOOP
            hit := regexp_instr(response COLLATE "C",
              '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', search_from);
            EXIT WHEN hit = 0;
            ids := array_append(ids, substr(response, hit, 36));
            -- Advance one character, not one match: UUID substrings can overlap.
            search_from := hit + 1;
          END LOOP;
          RETURN ids;
        END;
        $function$;
        CREATE INDEX commands_response_account_ids ON commands USING gin (auth_command_response_ids_v1(response));
        INSERT INTO auth_schema VALUES (3);
        """;
}
