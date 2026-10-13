#!/usr/bin/env bash
# Usage: deploy.sh <env> <image-tag>
# Rolls every service of cluster es-<env> to the image, waits, and only then records the tag in SSM.
# The app stack reads that parameter, so a later `cdk deploy` uses the image that is really running.
# CI logs are public: this script prints service names and fixed messages only. No task definition, no ARN,
# no image name and no AWS response; a failed AWS call shows its error code and nothing else.
# At most 10 services per cluster: that is the limit of one `ecs wait` and one `describe-services` call.
# DEPLOY_POLL_SECONDS (5) and DEPLOY_POLL_COUNT (12) set how long a roll-out that is still IN_PROGRESS after
# the wait is re-read before its outcome counts as unknown: 12 x 5 s = 60 s. The script's test shortens them.
set -euo pipefail

env_name="$1"
tag="$2"
cluster="es-${env_name}"
parameter="/es/${env_name}/app/image-tag"
poll_seconds="${DEPLOY_POLL_SECONDS:-5}"
poll_count="${DEPLOY_POLL_COUNT:-12}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# Every AWS call goes through here: its output and its error text stay in files under $work.
aws_quiet() {
  aws "$@" >"$work/stdout" 2>"$work/stderr"
}

aws_error_code() {
  local code
  code="$(sed -n 's/.*An error occurred (\([A-Za-z0-9.]*\)) when calling.*/\1/p' "$work/stderr" | head -n 1)"
  echo "${code:-no error code}"
}

fail() {
  echo "::error::$1 Nothing was recorded: ${parameter} still holds the previous tag."
  exit 1
}

aws_step() {
  local step="$1"
  shift
  aws_quiet "$@" || fail "${step} failed (AWS error: $(aws_error_code))."
}

# Leaves the service's task definition and deployments in $work/service.json.
describe_service() {
  aws_step "Reading the service ${1##*/}" ecs describe-services --cluster "$cluster" --services "$1" \
    --query 'services[0].{taskDefinition:taskDefinition,deployments:deployments[].{rolloutState:rolloutState,failedTasks:failedTasks}}' \
    --output json
  cp "$work/stdout" "$work/service.json"
}

# Prints deployed, pending or failed for the service in $work/service.json.
service_state() {
  jq -r --arg expected "$1" '
    if .taskDefinition != $expected or (.deployments | length) != 1 then "failed"
    elif .deployments[0].rolloutState == "IN_PROGRESS" then "pending"
    elif .deployments[0].rolloutState == "COMPLETED" and (.deployments[0].failedTasks // 0) == 0 then "deployed"
    else "failed" end' "$work/service.json" 2>/dev/null
}

record_tag() {
  aws_step "Recording the tag" ssm put-parameter --name "$parameter" --type String --overwrite --value "$tag"
}

aws_step "Reading the account" sts get-caller-identity --query Account --output text
account="$(cat "$work/stdout")"
image="${account}.dkr.ecr.${AWS_REGION}.amazonaws.com/extraspace/api:${tag}"

# Only a missing cluster means "first deploy". Any other failure must not record the tag.
declare -a services=()
if aws_quiet ecs list-services --cluster "$cluster" --query 'serviceArns' --output text; then
  # shellcheck disable=SC2013
  for arn in $(cat "$work/stdout"); do
    [ "$arn" = "None" ] || services+=("$arn")
  done
elif [ "$(aws_error_code)" != "ClusterNotFoundException" ]; then
  fail "Listing the services of ${cluster} failed (AWS error: $(aws_error_code))."
fi

if [ "${#services[@]}" -eq 0 ]; then
  record_tag
  echo "::notice::No services in ${cluster} yet. The tag is recorded; create the app stack by hand (setup guide B7)."
  exit 0
fi

# Register everything before the first update, so a failed register leaves no service half-rolled.
declare -a registered=()
for service in "${services[@]}"; do
  name="${service##*/}"
  describe_service "$service"
  current="$(jq -r '.taskDefinition // empty' "$work/service.json" 2>/dev/null)" \
    || fail "The answer for the service ${name} could not be read."
  [ -n "$current" ] || fail "The service ${name} has no task definition."
  aws_step "Reading the task definition of ${name}" ecs describe-task-definition --task-definition "$current" \
    --query 'taskDefinition' --output json
  cp "$work/stdout" "$work/current.json"
  jq -e '.containerDefinitions | type == "array"' "$work/current.json" >/dev/null 2>&1 \
    || fail "The task definition of ${name} could not be prepared."
  jq -e 'any(.containerDefinitions[]; .name == "app")' "$work/current.json" >/dev/null 2>&1 \
    || fail "The task definition of ${name} has no container named app."
  jq -e 'any(.containerDefinitions[] | select(.name == "app") | (.environment // [])[]; .name == "App__Version")' \
    "$work/current.json" >/dev/null 2>&1 \
    || fail "The app container of ${name} has no App__Version environment entry."
  jq --arg image "$image" --arg tag "$tag" '
      del(.taskDefinitionArn, .revision, .status, .requiresAttributes, .compatibilities, .registeredAt, .registeredBy, .deregisteredAt)
      | .containerDefinitions |= map(
          if .name == "app"
          then .image = $image | .environment |= map(if .name == "App__Version" then .value = $tag else . end)
          else . end)' \
    "$work/current.json" > "$work/new.json" 2>/dev/null \
    || fail "The task definition of ${name} could not be prepared."
  aws_step "Registering the task definition of ${name}" ecs register-task-definition \
    --cli-input-json "file://$work/new.json" --query 'taskDefinition.taskDefinitionArn' --output text
  registered+=("$(cat "$work/stdout")")
done

i=0
for service in "${services[@]}"; do
  aws_quiet ecs update-service --cluster "$cluster" --service "$service" --task-definition "${registered[$i]}" \
    || fail "Updating the service ${service##*/} failed (AWS error: $(aws_error_code)). Some services may already be rolling; check the cluster."
  echo "Rolling ${service##*/}."
  i=$((i + 1))
done

# One wait gives up after 10 minutes; a slow but healthy roll-out gets a second one.
stable=no
for attempt in 1 2; do
  if aws_quiet ecs wait services-stable --cluster "$cluster" --services "${services[@]}"; then
    stable=yes
    break
  fi
  echo "The services are not stable yet (wait ${attempt} of 2)."
done
if [ "$stable" != yes ]; then
  fail "The services of ${cluster} did not become stable in time, so the outcome is unknown. Check the ECS services before any cdk deploy."
fi

# The circuit breaker rolls a failed deploy back, and the wait above then succeeds on the OLD version.
# The wait can also end a moment before ECS marks the new deployment COMPLETED, so IN_PROGRESS is re-read.
failed=0
i=0
for service in "${services[@]}"; do
  name="${service##*/}"
  reads=0
  while :; do
    describe_service "$service"
    state="$(service_state "${registered[$i]}")" || fail "The answer for the service ${name} could not be read."
    if [ "$state" != pending ] || [ "$reads" -ge "$poll_count" ]; then break; fi
    reads=$((reads + 1))
    sleep "$poll_seconds"
  done
  case "$state" in
    deployed) ;;
    pending)
      echo "::error::${name}: the roll-out did not complete in time, so the outcome is unknown. Check the ECS service before any cdk deploy."
      failed=$((failed + 1)) ;;
    *)
      echo "::error::${name} was rolled back or did not start: the new tasks are not the running version. Check the ECS service events and the task's stop reason."
      failed=$((failed + 1)) ;;
  esac
  i=$((i + 1))
done
if [ "$failed" -gt 0 ]; then
  fail "${failed} service(s) of ${cluster} are not confirmed on the new version."
fi

record_tag
echo "Deployed ${tag} to ${cluster}."
