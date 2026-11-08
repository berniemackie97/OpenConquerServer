# Issue intake and delivery

The [shared issue-class definitions and completion gates](https://github.com/berniemackie97/OpenConquerClient/blob/main/docs/issue-governance.md)
apply to both OpenConquer repositories.

The server's [delivery board](https://github.com/users/berniemackie97/projects/5)
and [roadmap](https://github.com/berniemackie97/OpenConquerServer/issues/205)
own server implementation work. Cross-repository packet decisions are tracked
by the canonical client issue and linked from server changes, not duplicated
as independent decision tickets.

GitHub issue forms add the corresponding existing `type:*` label and request
project membership. Project Stage/Priority/Workstream and release milestone
assignments still require verification. A hosted test result cannot close a
release gate that depends on source-executable traces, database behavior or
live multi-session testing.
