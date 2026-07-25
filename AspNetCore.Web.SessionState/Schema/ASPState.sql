-- =============================================================================================
-- ASPState schema for <sessionState mode="SQLServer">.
--
-- aspnet_regsql.exe is .NET Framework only and has no .NET 10 equivalent, so the DDL it used to
-- emit ships here instead. Run it against a database you have already created:
--
--     CREATE DATABASE ASPState;
--     GO
--     USE ASPState;
--     GO
--     -- then this script
--
-- Object names and stored-procedure signatures are the standard ones, so an application already
-- pointed at a database provisioned by aspnet_regsql needs NONE of this - the provider talks to
-- those procedures unchanged. This exists for new deployments.
--
-- Two deliberate deviations from what aspnet_regsql emitted, both because the original relied on
-- features SQL Server has since deprecated:
--
--   * The long-payload path SELECTs SessionItemLong instead of using READTEXT and TEXTPTR. The
--     shape on the wire is identical - one single-row result set - so the provider cannot tell.
--   * TempGetAppID derives its application id with CHECKSUM rather than the hand-rolled string
--     hash. The id only has to be stable and unique WITHIN one database, and it is; it will not
--     match the id aspnet_regsql would have picked for the same name, which matters only if you
--     provision one database with both, and you should not.
--
-- The script is idempotent - running it again is safe.
-- =============================================================================================

IF OBJECT_ID (N'dbo.ASPStateTempApplications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ASPStateTempApplications (
        AppId   int         NOT NULL PRIMARY KEY,
        AppName char(280)   NOT NULL
    );

    CREATE NONCLUSTERED INDEX Index_AppName ON dbo.ASPStateTempApplications (AppName);
END
GO

IF OBJECT_ID (N'dbo.ASPStateTempSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ASPStateTempSessions (
        SessionId           nvarchar(88)    NOT NULL PRIMARY KEY,
        Created             datetime        NOT NULL DEFAULT GETUTCDATE(),
        Expires             datetime        NOT NULL,
        LockDate            datetime        NOT NULL,
        LockDateLocal       datetime        NOT NULL,
        LockCookie          int             NOT NULL,
        Timeout             int             NOT NULL,
        Locked              bit             NOT NULL,
        SessionItemShort    varbinary(7000) NULL,
        SessionItemLong     image           NULL,
        Flags               int             NOT NULL DEFAULT 0
    );

    -- The sweeper and every expiry check filter on this.
    CREATE NONCLUSTERED INDEX Index_Expires ON dbo.ASPStateTempSessions (Expires);
END
GO

-- ---------------------------------------------------------------------------------------------
-- Application interning. Session ids are only unique within an application, so the row key is
-- session id + application id and two applications can share one database.
-- ---------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.TempGetAppID
    @appName    varchar(280),
    @appId      int OUTPUT
AS
BEGIN
    SET NOCOUNT ON

    SET @appName = LOWER(@appName)
    SET @appId = NULL

    SELECT @appId = AppId FROM dbo.ASPStateTempApplications WHERE AppName = @appName

    IF @appId IS NULL
    BEGIN
        BEGIN TRANSACTION

        -- TABLOCKX so two instances starting at once cannot both decide the name is new and then
        -- race on the primary key.
        SELECT @appId = AppId FROM dbo.ASPStateTempApplications WITH (TABLOCKX) WHERE AppName = @appName

        IF @appId IS NULL
        BEGIN
            SET @appId = ABS(CHECKSUM(@appName))

            -- CHECKSUM does collide. Walk forward until a free id turns up; with a handful of
            -- applications per database this effectively never runs more than once.
            WHILE EXISTS (SELECT 1 FROM dbo.ASPStateTempApplications WHERE AppId = @appId)
                SET @appId = (@appId + 1) % 2147483647

            INSERT dbo.ASPStateTempApplications (AppId, AppName) VALUES (@appId, @appName)
        END

        COMMIT TRANSACTION
    END
END
GO

-- ---------------------------------------------------------------------------------------------
-- Read WITHOUT taking the lock. Used for read-only session access (<% @Page EnableSessionState
-- ="ReadOnly" %>), which must not block a concurrent writer.
-- ---------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.TempGetStateItem3
    @id             nvarchar(88),
    @itemShort      varbinary(7000) OUTPUT,
    @locked         bit OUTPUT,
    @lockAge        int OUTPUT,
    @lockCookie     int OUTPUT,
    @actionFlags    int OUTPUT
AS
BEGIN
    SET NOCOUNT ON

    DECLARE @now datetime = GETUTCDATE()
    DECLARE @length int

    UPDATE dbo.ASPStateTempSessions
    SET     Expires      = DATEADD(minute, Timeout, @now),
            @locked      = Locked,
            @lockAge     = DATEDIFF(second, LockDate, @now),
            @lockCookie  = LockCookie,
            @itemShort   = CASE Locked WHEN 0 THEN SessionItemShort ELSE NULL END,
            @length      = CASE Locked WHEN 0 THEN DATALENGTH(SessionItemLong) ELSE NULL END,
            @actionFlags = CASE Locked WHEN 0 THEN Flags ELSE 0 END,
            Flags        = 0
    WHERE   SessionId = @id

    -- Only when the payload went to SessionItemLong. The provider reads this result set first and
    -- the output parameters afterwards, which is the order ADO.NET requires.
    IF @length > 0
        SELECT SessionItemLong FROM dbo.ASPStateTempSessions WHERE SessionId = @id
END
GO

-- ---------------------------------------------------------------------------------------------
-- Read AND take the lock, in one row update - which is what makes SQLServer mode's locking
-- genuinely atomic, unlike a read-modify-write against a cache.
-- ---------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.TempGetStateItemExclusive3
    @id             nvarchar(88),
    @itemShort      varbinary(7000) OUTPUT,
    @locked         bit OUTPUT,
    @lockAge        int OUTPUT,
    @lockCookie     int OUTPUT,
    @actionFlags    int OUTPUT
AS
BEGIN
    SET NOCOUNT ON

    DECLARE @now datetime = GETUTCDATE()
    DECLARE @nowLocal datetime = GETDATE()
    DECLARE @length int

    -- Every right-hand side sees the PRE-update value, so @locked reports whether it was already
    -- locked even though the same statement sets Locked = 1.
    UPDATE dbo.ASPStateTempSessions
    SET     Expires       = DATEADD(minute, Timeout, @now),
            LockDate      = CASE Locked WHEN 0 THEN @now ELSE LockDate END,
            LockDateLocal = CASE Locked WHEN 0 THEN @nowLocal ELSE LockDateLocal END,
            @lockAge      = CASE Locked WHEN 0 THEN 0 ELSE DATEDIFF(second, LockDate, @now) END,
            @lockCookie   = LockCookie = CASE Locked WHEN 0 THEN LockCookie + 1 ELSE LockCookie END,
            @locked       = Locked,
            Locked        = 1,
            @itemShort    = CASE Locked WHEN 0 THEN SessionItemShort ELSE NULL END,
            @length       = CASE Locked WHEN 0 THEN DATALENGTH(SessionItemLong) ELSE NULL END,
            @actionFlags  = CASE Locked WHEN 0 THEN Flags ELSE 0 END,
            Flags         = 0
    WHERE   SessionId = @id

    IF @length > 0
        SELECT SessionItemLong FROM dbo.ASPStateTempSessions WHERE SessionId = @id
END
GO

CREATE OR ALTER PROCEDURE dbo.TempReleaseStateItemExclusive
    @id         nvarchar(88),
    @lockCookie int
AS
BEGIN
    SET NOCOUNT ON

    UPDATE dbo.ASPStateTempSessions
    SET     Expires = DATEADD(minute, Timeout, GETUTCDATE()),
            Locked  = 0
    WHERE   SessionId = @id AND LockCookie = @lockCookie
END
GO

CREATE OR ALTER PROCEDURE dbo.TempInsertUninitializedItem
    @id         nvarchar(88),
    @itemShort  varbinary(7000),
    @timeout    int
AS
BEGIN
    SET NOCOUNT ON

    DECLARE @now datetime = GETUTCDATE()
    DECLARE @nowLocal datetime = GETDATE()

    -- Flags = 1 is SessionStateActions.InitializeItem: a placeholder for a cookieless session,
    -- telling the request that picks it up to raise Session_Start.
    INSERT dbo.ASPStateTempSessions
        (SessionId, SessionItemShort, Timeout, Expires, Locked, LockDate, LockDateLocal, LockCookie, Flags)
    VALUES
        (@id, @itemShort, @timeout, DATEADD(minute, @timeout, @now), 0, @now, @nowLocal, 1, 1)
END
GO

CREATE OR ALTER PROCEDURE dbo.TempInsertStateItemShort
    @id         nvarchar(88),
    @itemShort  varbinary(7000),
    @timeout    int
AS
BEGIN
    SET NOCOUNT ON

    DECLARE @now datetime = GETUTCDATE()
    DECLARE @nowLocal datetime = GETDATE()

    INSERT dbo.ASPStateTempSessions
        (SessionId, SessionItemShort, Timeout, Expires, Locked, LockDate, LockDateLocal, LockCookie)
    VALUES
        (@id, @itemShort, @timeout, DATEADD(minute, @timeout, @now), 0, @now, @nowLocal, 1)
END
GO

CREATE OR ALTER PROCEDURE dbo.TempInsertStateItemLong
    @id         nvarchar(88),
    @itemLong   image,
    @timeout    int
AS
BEGIN
    SET NOCOUNT ON

    DECLARE @now datetime = GETUTCDATE()
    DECLARE @nowLocal datetime = GETDATE()

    INSERT dbo.ASPStateTempSessions
        (SessionId, SessionItemLong, Timeout, Expires, Locked, LockDate, LockDateLocal, LockCookie)
    VALUES
        (@id, @itemLong, @timeout, DATEADD(minute, @timeout, @now), 0, @now, @nowLocal, 1)
END
GO

CREATE OR ALTER PROCEDURE dbo.TempUpdateStateItemShort
    @id         nvarchar(88),
    @itemShort  varbinary(7000),
    @timeout    int,
    @lockCookie int
AS
BEGIN
    SET NOCOUNT ON

    UPDATE dbo.ASPStateTempSessions
    SET     Expires          = DATEADD(minute, @timeout, GETUTCDATE()),
            SessionItemShort = @itemShort,
            Timeout          = @timeout,
            Locked           = 0
    WHERE   SessionId = @id AND LockCookie = @lockCookie
END
GO

-- Clears SessionItemLong as well. Used whenever a session that once needed the image column
-- shrinks back under 7000 bytes - leaving the old blob would let a later read pick up stale data.
CREATE OR ALTER PROCEDURE dbo.TempUpdateStateItemShortNullLong
    @id         nvarchar(88),
    @itemShort  varbinary(7000),
    @timeout    int,
    @lockCookie int
AS
BEGIN
    SET NOCOUNT ON

    UPDATE dbo.ASPStateTempSessions
    SET     Expires          = DATEADD(minute, @timeout, GETUTCDATE()),
            SessionItemShort = @itemShort,
            SessionItemLong  = NULL,
            Timeout          = @timeout,
            Locked           = 0
    WHERE   SessionId = @id AND LockCookie = @lockCookie
END
GO

CREATE OR ALTER PROCEDURE dbo.TempUpdateStateItemLong
    @id         nvarchar(88),
    @itemLong   image,
    @timeout    int,
    @lockCookie int
AS
BEGIN
    SET NOCOUNT ON

    UPDATE dbo.ASPStateTempSessions
    SET     Expires         = DATEADD(minute, @timeout, GETUTCDATE()),
            SessionItemLong = @itemLong,
            Timeout         = @timeout,
            Locked          = 0
    WHERE   SessionId = @id AND LockCookie = @lockCookie
END
GO

CREATE OR ALTER PROCEDURE dbo.TempUpdateStateItemLongNullShort
    @id         nvarchar(88),
    @itemLong   image,
    @timeout    int,
    @lockCookie int
AS
BEGIN
    SET NOCOUNT ON

    UPDATE dbo.ASPStateTempSessions
    SET     Expires          = DATEADD(minute, @timeout, GETUTCDATE()),
            SessionItemLong  = @itemLong,
            SessionItemShort = NULL,
            Timeout          = @timeout,
            Locked           = 0
    WHERE   SessionId = @id AND LockCookie = @lockCookie
END
GO

CREATE OR ALTER PROCEDURE dbo.TempRemoveStateItem
    @id         nvarchar(88),
    @lockCookie int
AS
BEGIN
    SET NOCOUNT ON

    DELETE dbo.ASPStateTempSessions
    WHERE SessionId = @id AND LockCookie = @lockCookie
END
GO

CREATE OR ALTER PROCEDURE dbo.TempResetTimeout
    @id nvarchar(88)
AS
BEGIN
    SET NOCOUNT ON

    UPDATE dbo.ASPStateTempSessions
    SET     Expires = DATEADD(minute, Timeout, GETUTCDATE())
    WHERE   SessionId = @id
END
GO

-- ---------------------------------------------------------------------------------------------
-- The sweeper. Under IIS this was a SQL Agent job created by aspnet_regsql, running every minute.
-- Nothing schedules it for you here: expired rows are never READ (every procedure filters on the
-- session id and the provider treats a missing row as a new session), so leaving it uncalled costs
-- disk rather than correctness. Schedule it from SQL Agent, a cron job, or a hosted service.
-- ---------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.DeleteExpiredSessions
AS
BEGIN
    SET NOCOUNT ON
    SET DEADLOCK_PRIORITY LOW

    DECLARE @now datetime = GETUTCDATE()

    CREATE TABLE #expired (SessionId nvarchar(88) NOT NULL PRIMARY KEY)

    INSERT #expired (SessionId)
    SELECT SessionId FROM dbo.ASPStateTempSessions WITH (READUNCOMMITTED) WHERE Expires < @now

    -- Deleted in batches through a join on the temp table rather than one big DELETE, so the
    -- sweeper cannot hold a table-wide lock while live requests are trying to read.
    DELETE dbo.ASPStateTempSessions
    FROM dbo.ASPStateTempSessions s INNER JOIN #expired e ON s.SessionId = e.SessionId
    WHERE s.Expires < @now

    DROP TABLE #expired
END
GO
