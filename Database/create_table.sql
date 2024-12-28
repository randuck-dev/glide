CREATE TABLE DesiredContainerState (
    id TEXT NOT NULL PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    image TEXT NOT NULL,
    command TEXT NOT NULL,
    container_id TEXT NULL,
    ports TEXT NOT NULL
);
