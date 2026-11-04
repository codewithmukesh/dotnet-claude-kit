# Claude Code with and without the navigator: real A/B

Every task ran as a fresh headless Claude Code session (`claude -p`, model `sonnet`), once per arm, with the same prompt. **without** = built-in Grep, Glob, and Read. **with** = the same tools plus the navigator and a one-paragraph note to prefer it. Bash, edits, web, and subagents were off in both arms, and personal hooks, plugins, CLAUDE.md, and memory were isolated away. Wall time runs from sending the question to the final answer and includes every model round trip. Navigator startup is excluded (it warmed before the question was sent) and is reported by the main benchmark. Input tokens are what the API processed, including cache reads, tool definitions, and the system prompt. Accuracy is a blinded 0–100 score from a separate judge session against the navigator's raw output.

## Summary (all tasks per solution)

| Tier | Solution | Arm | Wall time (sum) | Median per task | Tool calls | Input tokens | Output tokens | Cost (USD) | Avg accuracy | Failed |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| small | clean-architecture | without | 112 s | 9.7 s | 23 | 238,446 | 10,426 | 0.34 | 59 | 0 |
| small | clean-architecture | with | 62 s | 4.6 s | 16 | 345,659 | 4,932 | 0.20 | 95 | 0 |
| small | eshop | without | 90 s | 7.0 s | 17 | 187,863 | 9,513 | 0.26 | 78 | 0 |
| small | eshop | with | 64 s | 4.9 s | 11 | 297,646 | 6,805 | 0.25 | 95 | 0 |
| medium | jellyfin | without | 168 s | 12.6 s | 27 | 409,257 | 19,438 | 0.69 | 70 | 0 |
| medium | jellyfin | with | 127 s | 9.8 s | 11 | 326,423 | 15,385 | 0.44 | 93 | 0 |
| large | orchardcore | without | 179 s | 15.4 s | 35 | 357,668 | 15,176 | 0.53 | 48 | 0 |
| large | orchardcore | with | 133 s | 9.8 s | 11 | 346,689 | 17,417 | 0.55 | 91 | 0 |
| xl | abp | without | 186 s | 15.7 s | 35 | 337,396 | 16,509 | 0.55 | 49 | 0 |
| xl | abp | with | 116 s | 11.0 s | 13 | 412,633 | 11,645 | 0.63 | 86 | 0 |

## clean-architecture

| Task | Wall s (without → with) | Tool calls | Input tokens | Cost USD | Accuracy | Navigator tools used |
|---|---:|---:|---:|---:|---:|---|
| locate-type | 5.7 → 2.8 | 1 → 1 | 13,337 → 25,874 | 0.013 → 0.013 | 60 → 100 | find_symbol×1 |
| interface-usages | 9.7 → 4.8 | 1 → 1 | 14,527 → 26,318 | 0.026 → 0.018 | 55 → 95 | find_references×1 |
| implementations | 6.2 → 4.3 | 1 → 1 | 13,326 → 25,845 | 0.015 → 0.013 | 40 → 100 | find_implementations×1 |
| outline-file | 6.9 → 4.2 | 1 → 1 | 13,575 → 26,259 | 0.018 → 0.017 | 88 → 95 | get_file_outline×1 |
| read-method | 8.8 → 3.1 | 3 → 1 | 29,067 → 25,874 | 0.023 → 0.014 | 88 → 97 | get_symbol_source×1 |
| find-callers | 11.1 → 4.8 | 4 → 1 | 22,216 → 25,976 | 0.029 → 0.015 | 30 → 100 | find_callers×1 |
| type-hierarchy | 15.3 → 4.1 | 3 → 1 | 21,767 → 25,892 | 0.027 → 0.014 | 88 → 95 | get_type_hierarchy×1 |
| project-graph | 8.2 → 4.6 | 2 → 1 | 16,988 → 26,660 | 0.033 → 0.018 | 55 → 80 | get_project_graph×1 |
| di-registrations | 10.3 → 6.3 | 1 → 1 | 16,774 → 26,687 | 0.037 → 0.020 | 62 → 92 | get_di_registrations×1 |
| endpoint-map | 19.9 → 15.8 | 5 → 6 | 59,069 → 83,182 | 0.086 → 0.038 | 30 → 95 | get_endpoint_map×1, get_workspace_status×1 |
| stack-trace | 10.3 → 7.0 | 1 → 1 | 17,800 → 27,092 | 0.036 → 0.024 | 55 → 95 | resolve_stack_trace×1 |

## eshop

| Task | Wall s (without → with) | Tool calls | Input tokens | Cost USD | Accuracy | Navigator tools used |
|---|---:|---:|---:|---:|---:|---|
| locate-type | 5.1 → 2.5 | 1 → 1 | 12,796 → 25,826 | 0.009 → 0.013 | 92 → 98 | find_symbol×1 |
| interface-usages | 5.2 → 6.6 | 1 → 1 | 13,155 → 26,297 | 0.014 → 0.017 | 72 → 95 | find_references×1 |
| implementations | 6.0 → 4.1 | 1 → 1 | 12,860 → 25,782 | 0.011 → 0.013 | 95 → 95 | find_implementations×1 |
| outline-file | 9.0 → 9.0 | 1 → 1 | 14,323 → 27,773 | 0.026 → 0.032 | 55 → 93 | get_file_outline×1 |
| read-method | 6.2 → 4.0 | 2 → 1 | 20,105 → 25,907 | 0.017 → 0.015 | 90 → 98 | get_symbol_source×1 |
| find-callers | 7.0 → 4.4 | 2 → 1 | 20,458 → 25,824 | 0.018 → 0.013 | 55 → 97 | find_callers×1 |
| type-hierarchy | 9.9 → 3.9 | 4 → 1 | 21,785 → 25,908 | 0.028 → 0.015 | 88 → 97 | get_type_hierarchy×1 |
| project-graph | 11.2 → 6.6 | 2 → 1 | 25,969 → 27,977 | 0.043 → 0.029 | 70 → 97 | get_project_graph×1 |
| di-registrations | 13.2 → 9.7 | 1 → 1 | 17,295 → 30,727 | 0.046 → 0.045 | 88 → 90 | get_di_registrations×1 |
| endpoint-map | 10.6 → 8.6 | 1 → 1 | 15,462 → 29,030 | 0.033 → 0.036 | 72 → 95 | get_endpoint_map×1 |
| stack-trace | 6.1 → 4.9 | 1 → 1 | 13,655 → 26,595 | 0.016 → 0.019 | 85 → 92 | resolve_stack_trace×1 |

## jellyfin

| Task | Wall s (without → with) | Tool calls | Input tokens | Cost USD | Accuracy | Navigator tools used |
|---|---:|---:|---:|---:|---:|---|
| locate-type | 6.8 → 2.6 | 1 → 1 | 12,971 → 25,941 | 0.010 → 0.013 | 100 → 100 | find_symbol×1 |
| interface-usages | 21.5 → 19.6 | 2 → 1 | 32,069 → 30,365 | 0.088 → 0.060 | 72 → 98 | find_references×1 |
| implementations | 9.2 → 5.4 | 2 → 1 | 23,913 → 25,926 | 0.025 → 0.013 | 95 → 97 | find_implementations×1 |
| outline-file | 22.5 → 20.7 | 1 → 1 | 20,822 → 37,810 | 0.080 → 0.099 | 70 → 93 | get_file_outline×1 |
| read-method | 4.1 → 18.4 | 1 → 1 | 16,995 → 25,960 | 0.028 → 0.014 | 93 → 98 | get_symbol_source×1 |
| find-callers | 33.3 → 9.2 | 6 → 1 | 140,940 → 27,769 | 0.189 → 0.029 | 40 → 100 | find_callers×1 |
| type-hierarchy | 11.4 → 7.6 | 3 → 1 | 40,469 → 26,147 | 0.069 → 0.019 | 72 → 90 | get_type_hierarchy×1 |
| project-graph | 11.1 → 10.1 | 2 → 1 | 35,391 → 30,456 | 0.070 → 0.045 | 72 → 97 | get_project_graph×1 |
| di-registrations | 17.7 → 9.8 | 4 → 1 | 34,057 → 31,205 | 0.049 → 0.045 | 40 → 80 | get_di_registrations×1 |
| endpoint-map | 17.5 → 16.7 | 3 → 1 | 25,169 → 37,540 | 0.046 → 0.084 | 40 → 80 | get_endpoint_map×1 |
| stack-trace | 12.6 → 7.1 | 2 → 1 | 26,461 → 27,304 | 0.034 → 0.024 | 72 → 85 | resolve_stack_trace×1 |

## orchardcore

| Task | Wall s (without → with) | Tool calls | Input tokens | Cost USD | Accuracy | Navigator tools used |
|---|---:|---:|---:|---:|---:|---|
| locate-type | 24.7 → 4.0 | 1 → 1 | 12,822 → 25,837 | 0.009 → 0.013 | 98 → 100 | find_symbol×1 |
| interface-usages | 15.4 → 35.7 | 2 → 1 | 23,252 → 38,852 | 0.037 → 0.132 | 8 → 96 | find_references×1 |
| implementations | 10.6 → 15.1 | 2 → 1 | 20,376 → 29,830 | 0.018 → 0.045 | 5 → 92 | find_implementations×1 |
| outline-file | 12.6 → 5.4 | 2 → 1 | 20,557 → 26,372 | 0.022 → 0.019 | 72 → 97 | get_file_outline×1 |
| read-method | 7.6 → 3.1 | 2 → 1 | 20,042 → 25,847 | 0.016 → 0.013 | 93 → 98 | get_symbol_source×1 |
| find-callers | 20.0 → 17.3 | 9 → 1 | 89,932 → 41,220 | 0.094 → 0.091 | 25 → 88 | find_callers×1 |
| type-hierarchy | 20.9 → 10.0 | 6 → 1 | 53,121 → 32,574 | 0.098 → 0.044 | 60 → 88 | get_type_hierarchy×1 |
| project-graph | 21.7 → 9.8 | 4 → 1 | 49,972 → 27,600 | 0.112 → 0.031 | 35 → 70 | get_project_graph×1 |
| di-registrations | 11.9 → 9.3 | 3 → 1 | 23,607 → 33,575 | 0.033 → 0.050 | 25 → 88 | get_di_registrations×1 |
| endpoint-map | 19.6 → 16.7 | 2 → 1 | 20,481 → 37,744 | 0.062 → 0.085 | 55 → 88 | get_endpoint_map×1 |
| stack-trace | 13.9 → 6.6 | 2 → 1 | 23,506 → 27,238 | 0.030 → 0.025 | 55 → 92 | resolve_stack_trace×1 |

## abp

| Task | Wall s (without → with) | Tool calls | Input tokens | Cost USD | Accuracy | Navigator tools used |
|---|---:|---:|---:|---:|---:|---|
| locate-type | 23.5 → 3.6 | 1 → 1 | 12,808 → 25,826 | 0.009 → 0.013 | 80 → 97 | find_symbol×1 |
| interface-usages | 16.5 → 16.7 | 3 → 3 | 33,827 → 95,104 | 0.040 → 0.185 | 15 → 62 | find_references×3 |
| implementations | 13.8 → 16.0 | 3 → 1 | 26,227 → 43,652 | 0.038 → 0.095 | 20 → 72 | find_implementations×1 |
| outline-file | 18.3 → 10.5 | 3 → 1 | 30,535 → 31,402 | 0.045 → 0.050 | 90 → 93 | get_file_outline×1 |
| read-method | 5.2 → 3.4 | 1 → 1 | 13,499 → 25,952 | 0.014 → 0.015 | 80 → 97 | get_symbol_source×1 |
| find-callers | 13.5 → 9.9 | 4 → 1 | 23,511 → 26,203 | 0.036 → 0.017 | 40 → 100 | find_callers×1 |
| type-hierarchy | 15.7 → 12.0 | 6 → 1 | 46,989 → 31,544 | 0.090 → 0.044 | 30 → 88 | get_type_hierarchy×1 |
| project-graph | 15.3 → 9.2 | 3 → 1 | 23,533 → 28,377 | 0.035 → 0.033 | 22 → 80 | get_project_graph×1 |
| di-registrations | 23.3 → 11.1 | 3 → 1 | 44,470 → 36,157 | 0.101 → 0.064 | 52 → 82 | get_di_registrations×1 |
| endpoint-map | 27.8 → 12.8 | 5 → 1 | 58,787 → 41,271 | 0.107 → 0.091 | 45 → 90 | get_endpoint_map×1 |
| stack-trace | 12.7 → 11.0 | 3 → 1 | 23,210 → 27,145 | 0.032 → 0.026 | 62 → 85 | resolve_stack_trace×1 |
