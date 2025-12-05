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
    }

    public void SaveStudent(Student student)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var deleteCmd = new SqliteCommand("DELETE FROM students WHERE chat_id = $chat_id;", connection);
        deleteCmd.Parameters.AddWithValue("$chat_id", student.ChatId);
        deleteCmd.ExecuteNonQuery();

        using var cmd = new SqliteCommand(@"INSERT INTO students(chat_id, name, institute, photo_file_id, description)
                                            VALUES ($chat_id, $name, $institute, $photo_file_id, $description);", connection);
        cmd.Parameters.AddWithValue("$chat_id", student.ChatId);
        cmd.Parameters.AddWithValue("$name", student.Name);
        cmd.Parameters.AddWithValue("$institute", student.Institute);
        cmd.Parameters.AddWithValue("$photo_file_id", (object?)student.PhotoFileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$description", (object?)student.Description ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public Student? GetRandomStudent(long excludeChatId)
{
    using var connection = new SqliteConnection(_connectionString);
    connection.Open();

    using var cmd = new SqliteCommand(@"
        SELECT chat_id, name, institute, photo_file_id, description
        FROM students
        WHERE chat_id != $excludeChatId
        ORDER BY RANDOM()
        LIMIT 1;", connection);
    
    cmd.Parameters.AddWithValue("$excludeChatId", excludeChatId); // исключаем себя

    using var reader = cmd.ExecuteReader(CommandBehavior.SingleRow);
    if (!reader.Read())
        return null; // если нет других анкет кроме исключенной

    var student = new Student // это нейронка так решила проблему, что GetRandomStudent не всегда возвращает значения
    {
        ChatId = reader.GetInt64(0),
        Name = reader.GetString(1),
        Institute = reader.GetString(2),
        PhotoFileId = reader.IsDBNull(3) ? null : reader.GetString(3),
        Description = reader.IsDBNull(4) ? null : reader.GetString(4)
    };

    return student; 
}
    public Student? GetStudentByChatId(long chatId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = new SqliteCommand(@"SELECT name, institute, photo_file_id, description
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
            Description = reader.IsDBNull(3) ? null : reader.GetString(3)
        };

        return student;
    }
}
