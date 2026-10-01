using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WManager.Knowledge
{
    internal sealed class SqliteKnowledgeStore : IDisposable
    {
        private IntPtr database;
        private readonly string path;
        private const int Row = 100;
        private const int Done = 101;

        public SqliteKnowledgeStore(string path, string fingerprint)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException("This SQLite backend currently supports Windows 10/11 only.");
            this.path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(this.path));
            int code = Native.sqlite3_open_v2(Utf8(this.path), out database, 0x00000002 | 0x00000004 | 0x00010000, IntPtr.Zero);
            if (code != 0)
            {
                string error = Error();
                Dispose();
                throw new IOException(error);
            }
            try
            {
                Native.sqlite3_busy_timeout(database, 5000);
                using (var version = Prepare("PRAGMA user_version"))
                {
                    version.Step();
                    int schema = version.Int(0);
                    if (schema != 0 && schema != 1) throw new InvalidOperationException("Unsupported knowledge database schema: " + schema);
                }
                Execute("PRAGMA foreign_keys=ON");
                Execute("PRAGMA journal_mode=WAL");
                Execute("PRAGMA synchronous=FULL");
                Execute("CREATE TABLE IF NOT EXISTS metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL)");
                Execute("CREATE TABLE IF NOT EXISTS documents(id TEXT PRIMARY KEY,title TEXT NOT NULL,source TEXT NOT NULL,hash TEXT NOT NULL,updated TEXT NOT NULL,chunk_count INTEGER NOT NULL)");
                Execute("CREATE TABLE IF NOT EXISTS chunks(id TEXT PRIMARY KEY,document_id TEXT NOT NULL REFERENCES documents(id) ON DELETE CASCADE,ordinal INTEGER NOT NULL,heading TEXT NOT NULL,text TEXT NOT NULL,vector BLOB NOT NULL)");
                Execute("CREATE INDEX IF NOT EXISTS idx_chunks_document ON chunks(document_id)");
                Execute("PRAGMA user_version=1");
                string existing = null;
                using (var query = Prepare("SELECT value FROM metadata WHERE key='index_fingerprint'"))
                    if (query.Step()) existing = query.Text(0);
                if (existing != null && existing != fingerprint && ListDocuments().Count > 0)
                    throw new InvalidOperationException("The embedding model or chunk settings differ from this knowledge base. Restore its original settings or use a new database to rebuild it.");
                if (existing == null || existing != fingerprint)
                {
                    using (var write = Prepare("INSERT OR REPLACE INTO metadata(key,value) VALUES('index_fingerprint',?)"))
                    { write.Bind(1, fingerprint); write.Step(); }
                }
            }
            catch { Dispose(); throw; }
        }

        public List<KnowledgeDocument> ListDocuments()
        {
            var result = new List<KnowledgeDocument>();
            using (var query = Prepare("SELECT id,title,source,hash,updated,chunk_count FROM documents ORDER BY title,id"))
                while (query.Step())
                    result.Add(new KnowledgeDocument
                    {
                        Id = query.Text(0), Title = query.Text(1), Source = query.Text(2),
                        ContentHash = query.Text(3), UpdatedUtc = query.Text(4), ChunkCount = query.Int(5)
                    });
            return result;
        }

        public List<StoredChunk> ReadChunks(string documentId, CancellationToken ct)
        {
            var result = new List<StoredChunk>();
            string sql = "SELECT id,document_id,ordinal,heading,text,vector FROM chunks";
            if (!string.IsNullOrEmpty(documentId)) sql += " WHERE document_id=?";
            using (var query = Prepare(sql))
            {
                if (!string.IsNullOrEmpty(documentId)) query.Bind(1, documentId);
                while (query.Step())
                {
                    ct.ThrowIfCancellationRequested();
                    byte[] blob = query.Blob(5);
                    if (blob.Length == 0 || blob.Length % sizeof(float) != 0) throw new InvalidDataException("Corrupt vector data.");
                    var vector = new float[blob.Length / sizeof(float)];
                    Buffer.BlockCopy(blob, 0, vector, 0, blob.Length);
                    result.Add(new StoredChunk
                    {
                        Id = query.Text(0), DocumentId = query.Text(1), Ordinal = query.Int(2),
                        Heading = query.Text(3), Text = query.Text(4), Vector = vector
                    });
                }
            }
            return result;
        }

        public void ReplaceDocument(KnowledgeDocument document, IReadOnlyList<StoredChunk> chunks, CancellationToken ct)
        {
            Execute("BEGIN IMMEDIATE");
            try
            {
                DeleteDocument(document.Id);
                using (var write = Prepare("INSERT INTO documents(id,title,source,hash,updated,chunk_count) VALUES(?,?,?,?,?,?)"))
                {
                    write.Bind(1, document.Id); write.Bind(2, document.Title); write.Bind(3, document.Source);
                    write.Bind(4, document.ContentHash); write.Bind(5, document.UpdatedUtc); write.Bind(6, chunks.Count); write.Step();
                }
                foreach (var chunk in chunks)
                {
                    ct.ThrowIfCancellationRequested();
                    byte[] vector = new byte[chunk.Vector.Length * sizeof(float)];
                    Buffer.BlockCopy(chunk.Vector, 0, vector, 0, vector.Length);
                    using (var write = Prepare("INSERT INTO chunks(id,document_id,ordinal,heading,text,vector) VALUES(?,?,?,?,?,?)"))
                    {
                        write.Bind(1, chunk.Id); write.Bind(2, document.Id); write.Bind(3, chunk.Ordinal);
                        write.Bind(4, chunk.Heading ?? string.Empty); write.Bind(5, chunk.Text); write.Bind(6, vector); write.Step();
                    }
                }
                ct.ThrowIfCancellationRequested();
                Execute("COMMIT");
            }
            catch { Execute("ROLLBACK"); throw; }
        }

        public void DeleteDocument(string id)
        {
            using (var write = Prepare("DELETE FROM documents WHERE id=?"))
            { write.Bind(1, id); write.Step(); }
        }

        public void Export(string destinationPath, CancellationToken ct)
        {
            string destination = Path.GetFullPath(destinationPath);
            if (string.Equals(destination, path, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Export to a different database path.");
            if (File.Exists(destination)) throw new IOException("The export destination already exists.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            IntPtr output = IntPtr.Zero;
            IntPtr backup = IntPtr.Zero;
            try
            {
                ct.ThrowIfCancellationRequested();
                int opened = Native.sqlite3_open_v2(Utf8(temporary), out output, 0x00000002 | 0x00000004, IntPtr.Zero);
                if (opened != 0) throw new IOException("Could not create the export database.");
                backup = Native.sqlite3_backup_init(output, Utf8("main"), database, Utf8("main"));
                if (backup == IntPtr.Zero) throw new IOException("Could not initialize SQLite backup.");
                int code;
                do
                {
                    ct.ThrowIfCancellationRequested();
                    code = Native.sqlite3_backup_step(backup, 256);
                    if (code != 0 && code != Done) throw new IOException("SQLite backup failed: " + code);
                } while (code != Done);
                int finished = Native.sqlite3_backup_finish(backup);
                backup = IntPtr.Zero;
                if (finished != 0) throw new IOException("SQLite backup could not finish.");
                int closed = Native.sqlite3_close_v2(output);
                output = IntPtr.Zero;
                if (closed != 0) throw new IOException("Could not close the exported database.");
                ct.ThrowIfCancellationRequested();
                File.Move(temporary, destination);
            }
            finally
            {
                if (backup != IntPtr.Zero) Native.sqlite3_backup_finish(backup);
                if (output != IntPtr.Zero) Native.sqlite3_close_v2(output);
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private void Execute(string sql) { using (var statement = Prepare(sql)) while (statement.Step()) { } }
        private Statement Prepare(string sql) => new Statement(this, sql);
        private string Error() => database == IntPtr.Zero ? "SQLite could not open the database." : ReadUtf8(Native.sqlite3_errmsg(database));

        public void Dispose()
        {
            if (database == IntPtr.Zero) return;
            Native.sqlite3_close_v2(database);
            database = IntPtr.Zero;
        }

        private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + "\0");
        private static string ReadUtf8(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero) return string.Empty;
            int length = 0;
            while (Marshal.ReadByte(pointer, length) != 0) length++;
            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        private sealed class Statement : IDisposable
        {
            private readonly SqliteKnowledgeStore store;
            private IntPtr handle;
            public Statement(SqliteKnowledgeStore store, string sql)
            {
                this.store = store;
                byte[] bytes = Utf8(sql);
                int code = Native.sqlite3_prepare_v2(store.database, bytes, bytes.Length, out handle, IntPtr.Zero);
                if (code != 0) { Dispose(); throw new IOException(store.Error()); }
            }
            public void Bind(int index, string value)
            {
                byte[] bytes = Utf8(value ?? string.Empty);
                Check(Native.sqlite3_bind_text(handle, index, bytes, bytes.Length - 1, new IntPtr(-1)));
            }
            public void Bind(int index, int value) => Check(Native.sqlite3_bind_int(handle, index, value));
            public void Bind(int index, byte[] value) => Check(Native.sqlite3_bind_blob(handle, index, value, value.Length, new IntPtr(-1)));
            public bool Step()
            {
                int code = Native.sqlite3_step(handle);
                if (code == Row) return true;
                if (code == Done) return false;
                throw new IOException(store.Error());
            }
            public int Int(int column) => Native.sqlite3_column_int(handle, column);
            public string Text(int column) => ReadUtf8(Native.sqlite3_column_text(handle, column));
            public byte[] Blob(int column)
            {
                int length = Native.sqlite3_column_bytes(handle, column);
                var bytes = new byte[length];
                if (length > 0) Marshal.Copy(Native.sqlite3_column_blob(handle, column), bytes, 0, length);
                return bytes;
            }
            private void Check(int code) { if (code != 0) throw new IOException(store.Error()); }
            public void Dispose() { if (handle != IntPtr.Zero) { Native.sqlite3_finalize(handle); handle = IntPtr.Zero; } }
        }

        private static class Native
        {
            private const string Library = "winsqlite3";
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_close_v2(IntPtr db);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_busy_timeout(IntPtr db, int ms);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_errmsg(IntPtr db);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int length, out IntPtr statement, IntPtr tail);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_finalize(IntPtr statement);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_step(IntPtr statement);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_text(IntPtr statement, int index, byte[] text, int length, IntPtr destructor);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_blob(IntPtr statement, int index, byte[] blob, int length, IntPtr destructor);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_int(IntPtr statement, int index, int value);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_column_blob(IntPtr statement, int column);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_bytes(IntPtr statement, int column);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_int(IntPtr statement, int column);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_backup_init(IntPtr destination, byte[] destinationName, IntPtr source, byte[] sourceName);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_backup_step(IntPtr backup, int pages);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_backup_finish(IntPtr backup);
        }
    }
}
