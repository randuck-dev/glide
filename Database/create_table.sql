CREATE TABLE DesiredContainerState (
    name TEXT NOT NULL UNIQUE,
    image TEXT NOT NULL,
    command TEXT NOT NULL,
    id TEXT NULL,
    container_port INT NOT NULL,
    host_port INT NOT NULL
);
