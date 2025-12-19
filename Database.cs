using System.Data;
using Microsoft.Data.Sqlite;

namespace DatingBot;

internal sealed class Database
{
    private readonly string _connectionString;

    public Database(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        EnsureDatabase(dbPath);
    }

    private void EnsureDatabase(string dbPath)
    {
        if (!File.Exists(dbPath))
        {
            using (File.Create(dbPath)) { }
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var cmdText = @"CREATE TABLE IF NOT EXISTS students (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            chat_id INTEGER NOT NULL,
            name TEXT NOT NULL,
            institute TEXT NOT NULL,
            photo_file_id TEXT,
            description TEXT
        );";

        using var command = new SqliteCommand(cmdText, connection);
        command.ExecuteNonQuery();

        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var pragmaCmd = new SqliteCommand("PRAGMA table_info(students);", connection);
        using var reader = pragmaCmd.ExecuteReader();
        while (reader.Read())
        {
            var columnName = reader.GetString(1);
            existingColumns.Add(columnName);
        }

        // каждый раз при добавлении нового поля у class Student нужно добавлять сюда проверку и создание столбца
        if (!existingColumns.Contains("description"))
        {
            using var alterCmd = new SqliteCommand(
                "ALTER TABLE students ADD COLUMN description TEXT;",
                connection);
            alterCmd.ExecuteNonQuery();
        }

        if (!existingColumns.Contains("username"))
        {
            using var alterCmd = new SqliteCommand(
                "ALTER TABLE students ADD COLUMN username TEXT;",
                connection);
            alterCmd.ExecuteNonQuery();
        }

        var likesTableCmd = @"CREATE TABLE IF NOT EXISTS likes (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            liker_chat_id INTEGER NOT NULL,
            liked_chat_id INTEGER NOT NULL,
            liked_at TEXT NOT NULL
        );";
        using var createLikesCmd = new SqliteCommand(likesTableCmd, connection);
        createLikesCmd.ExecuteNonQuery();

        var reportsTableCmd = @"CREATE TABLE IF NOT EXISTS reports (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            reporter_chat_id INTEGER NOT NULL,
            reported_chat_id INTEGER NOT NULL,
            reported_at TEXT NOT NULL
        );";
        using var createReportsCmd = new SqliteCommand(reportsTableCmd, connection);
        createReportsCmd.ExecuteNonQuery();
    }

    public void SaveStudent(Student student)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var deleteCmd = new SqliteCommand("DELETE FROM students WHERE chat_id = $chat_id;", connection);
        deleteCmd.Parameters.AddWithValue("$chat_id", student.ChatId);
        deleteCmd.ExecuteNonQuery();

        using var cmd = new SqliteCommand(@"INSERT INTO students(chat_id, name, institute, photo_file_id, description, username)
                                            VALUES ($chat_id, $name, $institute, $photo_file_id, $description, $username);", connection);
        cmd.Parameters.AddWithValue("$chat_id", student.ChatId);
        cmd.Parameters.AddWithValue("$name", student.Name);
        cmd.Parameters.AddWithValue("$institute", student.Institute);
        cmd.Parameters.AddWithValue("$photo_file_id", (object?)student.PhotoFileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$description", (object?)student.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$username", (object?)student.Username ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public Student? GetRandomStudent(long excludeChatId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = new SqliteCommand(@"
            SELECT chat_id, name, institute, photo_file_id, description, username
            FROM students
            WHERE chat_id != $excludeChatId
              AND chat_id NOT IN (
                  SELECT reported_chat_id FROM reports WHERE reporter_chat_id = $excludeChatId
              )
              AND chat_id NOT IN (
                  SELECT reporter_chat_id FROM reports WHERE reported_chat_id = $excludeChatId
              )
            ORDER BY RANDOM()
            LIMIT 1;", connection);
        
        cmd.Parameters.AddWithValue("$excludeChatId", excludeChatId); // исключаем себя и заблокированных

        using var reader = cmd.ExecuteReader(CommandBehavior.SingleRow);
        if (!reader.Read())
            return null; // если нет других анкет кроме исключенной

        var student = new Student
        {
            ChatId = reader.GetInt64(0),
            Name = reader.GetString(1),
            Institute = reader.GetString(2),
            PhotoFileId = reader.IsDBNull(3) ? null : reader.GetString(3),
            Description = reader.IsDBNull(4) ? null : reader.GetString(4),
            Username = reader.IsDBNull(5) ? null : reader.GetString(5)
        };

        return student; 
    }
    public Student? GetStudentByChatId(long chatId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = new SqliteCommand(@"SELECT name, institute, photo_file_id, description, username
                                           FROM students
                                           WHERE chat_id = $chat_id
                                           ORDER BY id DESC
                                           LIMIT 1;", connection);
        cmd.Parameters.AddWithValue("$chat_id", chatId);

        using var reader = cmd.ExecuteReader(CommandBehavior.SingleRow);
        if (!reader.Read())
            return null;

        var student = new Student
        {
            ChatId = chatId,
            Name = reader.GetString(0),
            Institute = reader.GetString(1),
            PhotoFileId = reader.IsDBNull(2) ? null : reader.GetString(2),
            Description = reader.IsDBNull(3) ? null : reader.GetString(3),
            Username = reader.IsDBNull(4) ? null : reader.GetString(4)
        };

        return student;
    }

    /// <summary>
    /// Проверяет, можно ли лайкнуть пользователя (не чаще раза в сутки)
    /// </summary>
    public bool CanLike(long likerChatId, long likedChatId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var oneDayAgo = DateTime.UtcNow.AddDays(-1).ToString("o");
        using var cmd = new SqliteCommand(@"
            SELECT COUNT(*)
            FROM likes
            WHERE liker_chat_id = $liker_chat_id
              AND liked_chat_id = $liked_chat_id
              AND liked_at > $one_day_ago;", connection);
        cmd.Parameters.AddWithValue("$liker_chat_id", likerChatId);
        cmd.Parameters.AddWithValue("$liked_chat_id", likedChatId);
        cmd.Parameters.AddWithValue("$one_day_ago", oneDayAgo);

        var count = Convert.ToInt64(cmd.ExecuteScalar());
        return count == 0;
    }

    /// <summary>
    /// Сохраняет лайк в базу данных
    /// </summary>
    public void SaveLike(long likerChatId, long likedChatId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = new SqliteCommand(@"
            INSERT INTO likes(liker_chat_id, liked_chat_id, liked_at)
            VALUES ($liker_chat_id, $liked_chat_id, $liked_at);", connection);
        cmd.Parameters.AddWithValue("$liker_chat_id", likerChatId);
        cmd.Parameters.AddWithValue("$liked_chat_id", likedChatId);
        cmd.Parameters.AddWithValue("$liked_at", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Проверяет, есть ли взаимный лайк (матч)
    /// </summary>
    public bool HasMutualLike(long user1ChatId, long user2ChatId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = new SqliteCommand(@"
            SELECT COUNT(*)
            FROM likes
            WHERE liker_chat_id = $user2
              AND liked_chat_id = $user1;", connection);
        cmd.Parameters.AddWithValue("$user1", user1ChatId);
        cmd.Parameters.AddWithValue("$user2", user2ChatId);

        var count = Convert.ToInt64(cmd.ExecuteScalar());
        return count > 0;
    }

    /// <summary>
    /// Получает количество лайков, которые получил пользователь
    /// </summary>
    public int GetLikesCount(long chatId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = new SqliteCommand(@"
            SELECT COUNT(*)
            FROM likes
            WHERE liked_chat_id = $chat_id;", connection);
        cmd.Parameters.AddWithValue("$chat_id", chatId);

        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Сохраняет жалобу (добавляет в черный список)
    /// </summary>
    public void SaveReport(long reporterChatId, long reportedChatId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = new SqliteCommand(@"
            INSERT INTO reports(reporter_chat_id, reported_chat_id, reported_at)
            VALUES ($reporter_chat_id, $reported_chat_id, $reported_at);", connection);
        cmd.Parameters.AddWithValue("$reporter_chat_id", reporterChatId);
        cmd.Parameters.AddWithValue("$reported_chat_id", reportedChatId);
        cmd.Parameters.AddWithValue("$reported_at", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Удаляет все жалобы, отправленные пользователем
    /// </summary>
    public void DeleteAllReports(long reporterChatId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = new SqliteCommand(@"
            DELETE FROM reports
            WHERE reporter_chat_id = $reporter_chat_id;", connection);
        cmd.Parameters.AddWithValue("$reporter_chat_id", reporterChatId);
        cmd.ExecuteNonQuery();
    }
}
