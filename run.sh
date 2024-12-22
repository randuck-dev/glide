#!/bin/bash

CONTAINER_ID="0d"

while true; do
  echo "Starting container $CONTAINER_ID..."
  docker start $CONTAINER_ID
  sleep 5

  echo "Stopping container $CONTAINER_ID..."
  docker stop $CONTAINER_ID
  sleep 5

  echo "Container $CONTAINER_ID has been stopped."
done
