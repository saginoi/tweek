#!/bin/bash

# Abort script on error
set -e

RED='\033[0;31m'
GREEN='\033[0;32m'
NO_COLOR='\033[0m'

function get_exit_code() {
  local EXIT_CODE=$(docker-compose ps -q $@ 2> /dev/null | xargs -n 1 docker inspect --format='{{json .State.ExitCode}}')

  local COLOR=$GREEN
  if [ $((EXIT_CODE)) -ne 0 ]; then local COLOR=$RED; fi
  
  echo -e "${COLOR}$@ exited with code ${EXIT_CODE}${NO_COLOR}"

  return $((EXIT_CODE))
}

# prepare
echo 'building images'
docker-compose build &
docker-compose down --remove-orphans &
wait 


# run tests
echo 'running integration tests'
docker-compose up smoke-tests e2e-integration

get_exit_code smoke-tests
if [ $? -ne 0 ]; then exit $?; fi

get_exit_code e2e-integration
if [ $? -ne 0 ]; then exit $?; fi

docker-compose up e2e-ui
get_exit_code e2e-ui
if [ $? -ne 0 ]; then exit $?; fi
