#!/usr/bin/env bash
# Tests deploy.sh with a fake `aws` on PATH. Usage: deploy-test.sh [path-to-deploy.sh]
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
script="${1:-$here/deploy.sh}"
failures=0

api="arn:aws:ecs:x:123456789012:service/es-test/es-test-api"
jobs="arn:aws:ecs:x:123456789012:service/es-test/es-test-jobs"
new_api="arn:aws:ecs:x:123456789012:task-definition/es-test-api:2"
new_jobs="arn:aws:ecs:x:123456789012:task-definition/es-test-jobs:2"

# The fake keeps its state in $FAKE_DIR. Every error and every write answer carries the marker
# fake-response-body, so a case can prove that no AWS text reaches the log.
write_fake() {
  cat > "$1" <<'FAKE'
#!/usr/bin/env bash
d="$FAKE_DIR"
echo "$*" >> "$d/calls.log"
arg() {
  local flag="$1"
  shift
  while [ $# -gt 1 ]; do
    if [ "$1" = "$flag" ]; then echo "$2"; return; fi
    shift
  done
}
aws_error() {
  echo "An error occurred ($1) when calling the $2 operation: fake-response-body" >&2
  exit 254
}
case "$1 $2" in
  "sts get-caller-identity") echo "123456789012" ;;
  "ecs list-services")
    case "$(cat "$d/list-mode")" in
      denied) aws_error AccessDeniedException ListServices ;;
      missing) aws_error ClusterNotFoundException ListServices ;;
      *) cat "$d/services" ;;
    esac ;;
  "ecs describe-services")
    name="$(arg --services "$@")"; name="${name##*/}"
    old="arn:aws:ecs:x:123456789012:task-definition/$name:1"
    done_one='{"rolloutState":"COMPLETED","failedTasks":0}'
    if [ ! -f "$d/updated-$name" ]; then
      definition="$old"; deployments="[$done_one]"
    elif [ ! -f "$d/settled" ]; then
      # Until a wait has succeeded the roll-out is still running.
      definition="$(cat "$d/updated-$name")"; deployments="[{\"rolloutState\":\"IN_PROGRESS\",\"failedTasks\":0},$done_one]"
    else
      reads=$(( $(cat "$d/reads-$name" 2>/dev/null || echo 0) + 1 ))
      echo "$reads" > "$d/reads-$name"
      definition="$(cat "$d/updated-$name")"; deployments="[$done_one]"
      case "$(cat "$d/outcome-$name" 2>/dev/null)" in
        rolled_back) definition="$old" ;;
        # in_progress_reads: that many reads are IN_PROGRESS, then COMPLETED. The cap of 50 ends an endless poll as a success.
        in_progress) if [ "$reads" -le "$(cat "$d/in-progress-reads")" ] && [ "$reads" -le 50 ]; then deployments='[{"rolloutState":"IN_PROGRESS","failedTasks":0}]'; fi ;;
        rollout_failed) deployments='[{"rolloutState":"FAILED","failedTasks":0}]' ;;
        failed_tasks) deployments='[{"rolloutState":"COMPLETED","failedTasks":2}]' ;;
        two_deployments) deployments="[$done_one,$done_one]" ;;
      esac
    fi
    echo "{\"taskDefinition\":\"$definition\",\"deployments\":$deployments}" ;;
  "ecs describe-task-definition")
    family="$(arg --task-definition "$@")"; family="${family##*/}"; family="${family%:*}"
    if [ -f "$d/task-definition-raw" ]; then cat "$d/task-definition-raw"; else jq --arg family "$family" '.family = $family' "$d/task-definition.json"; fi ;;
  "ecs register-task-definition")
    file="$(arg --cli-input-json "$@")"; file="${file#file://}"
    family="$(jq -r '.family' "$file")"
    if [ "$(cat "$d/register-fails" 2>/dev/null)" = "$family" ]; then aws_error ClientException RegisterTaskDefinition; fi
    cp "$file" "$d/registered-$family.json"
    echo "arn:aws:ecs:x:123456789012:task-definition/$family:2" ;;
  "ecs update-service")
    name="$(arg --service "$@")"
    if [ "$(cat "$d/update-fails" 2>/dev/null)" = "${name##*/}" ]; then aws_error ServiceNotActiveException UpdateService; fi
    arg --task-definition "$@" > "$d/updated-${name##*/}"
    echo '{"service":{"serviceArn":"fake-response-body"}}' ;;
  "ecs wait")
    count=$(( $(cat "$d/wait-count" 2>/dev/null || echo 0) + 1 ))
    echo "$count" > "$d/wait-count"
    if [ "$count" -le "$(cat "$d/wait-failures")" ]; then
      echo "Waiter ServicesStable failed: Max attempts exceeded fake-response-body" >&2
      exit 255
    fi
    touch "$d/settled" ;;
  "ssm put-parameter")
    touch "$d/tag-written"
    echo '{"Version":2,"Tier":"fake-response-body"}' ;;
esac
FAKE
  chmod +x "$1"
}

begin() {
  name="$1"
  work="$(mktemp -d)"
  ok=1
  why=""
  mkdir -p "$work/bin"
  write_fake "$work/bin/aws"
  : > "$work/calls.log"
  echo ok > "$work/list-mode"
  echo "$api" > "$work/services"
  echo 0 > "$work/wait-failures"
  echo '{"family":"x","taskDefinitionArn":"old","revision":1,"status":"ACTIVE","containerDefinitions":[{"name":"app","image":"old-image","environment":[{"name":"App__Version","value":"old"},{"name":"App__Role","value":"All"}]}]}' > "$work/task-definition.json"
}

must() { if ! "$@" >/dev/null 2>&1; then ok=0; why="$why | expected: $*"; fi; }
must_not() { if "$@" >/dev/null 2>&1; then ok=0; why="$why | not expected: $*"; fi; }
out_has() { grep -qF -- "$1" "$work/out.log"; }
calls_have() { grep -q -- "$1" "$work/calls.log"; }
first_call() { grep -n -- "$1" "$work/calls.log" | head -n 1 | cut -d: -f1; }
last_call() { grep -n -- "$1" "$work/calls.log" | tail -n 1 | cut -d: -f1; }
before() { [ -n "$1" ] && [ -n "$2" ] && [ "$1" -lt "$2" ]; }
equals() { [ "$1" = "$2" ]; }
registered() { jq -r "$2" "$work/registered-$1.json"; }
no_roll_out() { must_not calls_have '^ecs \(register-task-definition\|update-service\|wait\)'; }

run() {
  local expect_exit="$1" expect_tag_written="$2"
  set +e
  DEPLOY_POLL_SECONDS=0 DEPLOY_POLL_COUNT=3 FAKE_DIR="$work" PATH="$work/bin:$PATH" AWS_REGION=ap-southeast-1 "$script" test abc123 > "$work/out.log" 2>&1
  code=$?
  set -e
  must equals "$code" "$expect_exit"
  if [ "$expect_tag_written" = yes ]; then
    must test -f "$work/tag-written"
  else
    must_not calls_have '^ssm put-parameter'
  fi
  # A roll-out records the tag only after a wait.
  if calls_have '^ecs update-service' && calls_have '^ssm put-parameter'; then
    must before "$(last_call '^ecs wait')" "$(first_call '^ssm put-parameter')"
  fi
  # Public log: no account id, no image name, no task definition, no AWS answer or error text.
  must_not out_has '123456789012'
  must_not out_has 'dkr.ecr'
  must_not out_has 'old-image'
  must_not out_has '"environment"'
  must_not out_has 'fake-response-body'
  must_not out_has 'An error occurred'
}

finish() {
  if [ "$ok" = 1 ]; then
    echo "ok   $name"
  else
    echo "FAIL $name (exit $code)$why"
    sed 's/^/     /' "$work/out.log"
    failures=$((failures + 1))
  fi
  rm -rf "$work"
}

begin healthy_deploy_records_the_tag
run 0 yes
must equals "$(registered es-test-api '.containerDefinitions[0].image')" "123456789012.dkr.ecr.ap-southeast-1.amazonaws.com/extraspace/api:abc123"
must equals "$(registered es-test-api '.containerDefinitions[0].environment[] | select(.name=="App__Version").value')" abc123
must equals "$(registered es-test-api '.containerDefinitions[0].environment[] | select(.name=="App__Role").value')" All
must equals "$(registered es-test-api 'has("taskDefinitionArn") or has("revision") or has("status")')" false
must calls_have "^ecs update-service .*--service $api .*--task-definition $new_api\$"
must calls_have '^ecs wait services-stable'
finish

begin rolled_back_deploy_fails_and_keeps_the_old_tag
echo rolled_back > "$work/outcome-es-test-api"
run 1 no
must out_has '::error::es-test-api was rolled back'
finish

begin no_services_yet_records_the_tag_only
echo "None" > "$work/services"
run 0 yes
no_roll_out
finish

begin aws_error_on_list_fails_and_records_nothing
echo denied > "$work/list-mode"
run 1 no
no_roll_out
must out_has '::error::Listing the services'
must out_has 'AccessDeniedException'
finish

begin missing_cluster_records_the_tag_only
echo missing > "$work/list-mode"
run 0 yes
no_roll_out
finish

begin empty_list_records_the_tag_only
: > "$work/services"
run 0 yes
no_roll_out
finish

begin wait_that_fails_twice_fails_and_records_nothing
echo 2 > "$work/wait-failures"
run 1 no
must equals "$(cat "$work/wait-count")" 2
must out_has 'the outcome is unknown'
must_not out_has 'was rolled back'
finish

begin wait_that_fails_once_is_tried_again
echo 1 > "$work/wait-failures"
run 0 yes
must equals "$(cat "$work/wait-count")" 2
finish

begin two_services_both_roll
printf '%s\t%s\n' "$api" "$jobs" > "$work/services"
run 0 yes
must equals "$(registered es-test-api '.containerDefinitions[0].environment[] | select(.name=="App__Version").value')" abc123
must equals "$(registered es-test-jobs '.containerDefinitions[0].environment[] | select(.name=="App__Version").value')" abc123
must calls_have "^ecs update-service .*--service $api .*--task-definition $new_api\$"
must calls_have "^ecs update-service .*--service $jobs .*--task-definition $new_jobs\$"
must calls_have "^ecs wait services-stable .*--services $api $jobs\$"
must before "$(last_call '^ecs register-task-definition')" "$(first_call '^ecs update-service')"
must out_has 'Rolling es-test-api.'
must out_has 'Rolling es-test-jobs.'
finish

begin two_services_one_rolled_back_fails_and_names_it
printf '%s\t%s\n' "$api" "$jobs" > "$work/services"
echo rolled_back > "$work/outcome-es-test-jobs"
run 1 no
must out_has '::error::es-test-jobs was rolled back'
must_not out_has '::error::es-test-api was rolled back'
finish

begin two_services_both_rolled_back_are_both_named
printf '%s\t%s\n' "$api" "$jobs" > "$work/services"
echo rolled_back > "$work/outcome-es-test-api"
echo rolled_back > "$work/outcome-es-test-jobs"
run 1 no
must out_has '::error::es-test-api was rolled back'
must out_has '::error::es-test-jobs was rolled back'
finish

begin in_progress_then_completed_records_the_tag
echo in_progress > "$work/outcome-es-test-api"
echo 2 > "$work/in-progress-reads"
run 0 yes
must equals "$(cat "$work/reads-es-test-api")" 3
finish

# Replaces rollout_not_completed_fails_and_records_nothing: still exit 1 and no tag, with its own message.
begin in_progress_forever_fails_with_the_unknown_message
echo in_progress > "$work/outcome-es-test-api"
echo 1000 > "$work/in-progress-reads"
run 1 no
must equals "$(cat "$work/reads-es-test-api")" 4
must out_has '::error::es-test-api: the roll-out did not complete in time, so the outcome is unknown.'
must_not out_has 'was rolled back'
finish

begin rollout_failed_fails_at_once
echo rollout_failed > "$work/outcome-es-test-api"
run 1 no
must equals "$(cat "$work/reads-es-test-api")" 1
must out_has '::error::es-test-api was rolled back or did not start'
finish

begin failed_tasks_fail_and_record_nothing
echo failed_tasks > "$work/outcome-es-test-api"
run 1 no
must out_has '::error::es-test-api was rolled back or did not start'
finish

begin second_deployment_still_listed_fails_and_records_nothing
echo two_deployments > "$work/outcome-es-test-api"
run 1 no
must out_has '::error::es-test-api was rolled back or did not start'
finish

begin only_the_app_container_is_changed
echo '{"family":"x","containerDefinitions":[{"name":"sidecar","image":"sidecar-image","environment":[{"name":"App__Version","value":"sidecar-version"}]},{"name":"app","image":"old-image","environment":[{"name":"App__Version","value":"old"},{"name":"App__Role","value":"All"}]}]}' > "$work/task-definition.json"
run 0 yes
must equals "$(registered es-test-api '.containerDefinitions | length')" 2
must equals "$(registered es-test-api '.containerDefinitions[0].image')" sidecar-image
must equals "$(registered es-test-api '.containerDefinitions[0].environment[0].value')" sidecar-version
must equals "$(registered es-test-api '.containerDefinitions[1].image')" "123456789012.dkr.ecr.ap-southeast-1.amazonaws.com/extraspace/api:abc123"
must equals "$(registered es-test-api '.containerDefinitions[1].environment[] | select(.name=="App__Version").value')" abc123
must equals "$(registered es-test-api '.containerDefinitions[1].environment[] | select(.name=="App__Role").value')" All
finish

begin no_app_container_fails_and_registers_nothing
echo '{"family":"x","containerDefinitions":[{"name":"web","image":"old-image","environment":[{"name":"App__Version","value":"old"}]}]}' > "$work/task-definition.json"
run 1 no
no_roll_out
must out_has '::error::The task definition of es-test-api has no container named app.'
finish

begin app_without_a_version_entry_fails_and_registers_nothing
echo '{"family":"x","containerDefinitions":[{"name":"app","image":"old-image","environment":[{"name":"App__Role","value":"All"}]}]}' > "$work/task-definition.json"
run 1 no
no_roll_out
must out_has '::error::The app container of es-test-api has no App__Version environment entry.'
finish

begin failed_register_updates_no_service
printf '%s\t%s\n' "$api" "$jobs" > "$work/services"
echo es-test-jobs > "$work/register-fails"
run 1 no
must_not calls_have '^ecs \(update-service\|wait\)'
must out_has '::error::Registering the task definition of es-test-jobs failed (AWS error: ClientException).'
finish

begin failed_update_says_services_may_be_rolling
printf '%s\t%s\n' "$api" "$jobs" > "$work/services"
echo es-test-jobs > "$work/update-fails"
run 1 no
must_not calls_have '^ecs wait'
must out_has '::error::Updating the service es-test-jobs failed (AWS error: ServiceNotActiveException). Some services may already be rolling; check the cluster.'
finish

begin task_definition_that_is_not_json_fails_with_a_message
echo 'this is not json fake-response-body' > "$work/task-definition-raw"
run 1 no
no_roll_out
must out_has '::error::The task definition of es-test-api could not be prepared.'
finish

[ "$failures" = 0 ] || { echo "$failures case(s) failed"; exit 1; }
echo "All deploy.sh cases passed."
