#!/bin/bash

# List all container IDs
container_ids=$(docker ps -aq)

# Check if there are any containers to remove
if [ -n "$container_ids" ]; then
  # Force remove all containers
  docker rm -f $container_ids
  echo "All containers have been removed."
else
  echo "No containers to remove."
fi
