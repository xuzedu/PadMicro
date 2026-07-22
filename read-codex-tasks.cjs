const { DatabaseSync } = require('node:sqlite');

const db = new DatabaseSync(process.argv[2], { readOnly: true });
try {
  const rows = db.prepare(`
    SELECT id, cwd, COALESCE(recency_at_ms, updated_at * 1000) AS recency, model
    FROM threads
    WHERE archived = 0
      AND (source IS NULL OR source NOT LIKE '{%')
    ORDER BY id DESC
  `).all();
  process.stdout.write(JSON.stringify(rows));
} finally {
  db.close();
}
