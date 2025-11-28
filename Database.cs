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
            photo_file_id TEXT
        );";

        using var command = new SqliteCommand(cmdText, connection);
        command.ExecuteNonQuery();
    }

    public void SaveStudent(Student student)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = new SqliteCommand(@"INSERT INTO students(chat_id, name, institute, photo_file_id)
                                            VALUES ($chat_id, $name, $institute, $photo_file_id);", connection);
        cmd.Parameters.AddWithValue("$chat_id", student.ChatId);
        cmd.Parameters.AddWithValue("$name", student.Name);
        cmd.Parameters.AddWithValue("$institute", student.Institute);
        cmd.Parameters.AddWithValue("$photo_file_id", (object?)student.PhotoFileId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public Student? GetStudentByChatId(long chatId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = new SqliteCommand(@"SELECT name, institute, photo_file_id
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
            PhotoFileId = reader.IsDBNull(2) ? null : reader.GetString(2)
        };

        return student;
    }
}
