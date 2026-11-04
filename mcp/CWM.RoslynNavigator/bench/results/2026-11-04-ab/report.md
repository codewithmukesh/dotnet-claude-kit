# Claude Code with and without the navigator: real A/B

Every task ran as a fresh headless Claude Code session (`claude -p`, model `sonnet`), once per arm, with the same prompt. **without** = built-in Grep, Glob, and Read. **with** = the same tools plus the navigator and a one-paragraph note to prefer it. Bash, edits, web, and subagents were off in both arms, and personal hooks, plugins, CLAUDE.md, and memory were isolated away. Wall time runs from sending the question to the final answer and includes every model round trip. Navigator startup is excluded (it warmed before the question was sent) and is reported by the main benchmark. Input tokens are what the API processed, including cache reads, tool definitions, and the system prompt. Accuracy is a blinded 0–100 score from a separate judge session against the navigator's raw output.

## Summary (all tasks per solution)

| Tier | Solution | Arm | Wall time (sum) | Median per task | Tool calls | Input tokens | Output tokens | Cost (USD) | Avg accuracy | Failed |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| small | clean-architecture | without | 92 s | 7.8 s | 22 | 220,197 | 9,497 | 0.23 | 61 | 0 |
| small | clean-architecture | with | 56 s | 3.5 s | 14 | 428,104 | 5,373 | 0.13 | 95 | 0 |
| small | eshop | without | 83 s | 6.7 s | 19 | 205,779 | 8,975 | 0.18 | 78 | 0 |
| small | eshop | with | 56 s | 4.4 s | 11 | 392,474 | 6,621 | 0.11 | 96 | 0 |
| medium | jellyfin | without | 161 s | 16.0 s | 28 | 356,050 | 20,904 | 0.62 | 66 | 0 |
| medium | jellyfin | with | 106 s | 9.1 s | 13 | 448,712 | 14,961 | 0.27 | 91 | 0 |
| large | orchardcore | without | 163 s | 15.4 s | 32 | 313,914 | 14,554 | 0.44 | 46 | 0 |
| large | orchardcore | with | 135 s | 10.1 s | 20 | 616,831 | 14,262 | 0.45 | 85 | 0 |
| xl | abp | without | 155 s | 14.6 s | 27 | 263,709 | 14,532 | 0.41 | 52 | 0 |
| xl | abp | with | 94 s | 7.2 s | 15 | 498,902 | 9,853 | 0.33 | 85 | 0 |

## clean-architecture

| Task | Wall s (without → with) | Tool calls | Input tokens | Cost USD | Accuracy | Navigator tools used |
|---|---:|---:|---:|---:|---:|---|
| locate-type | 4.5 → 2.4 | 1 → 1 | 13,337 → 34,366 | 0.006 → 0.005 | 80 → 100 | find_symbol×1 |
| interface-usages | 7.2 → 4.8 | 1 → 1 | 14,527 → 34,855 | 0.019 → 0.011 | 55 → 80 | find_references×1 |
| implementations | 6.0 → 3.5 | 1 → 1 | 13,313 → 34,359 | 0.009 → 0.005 | 40 → 100 | find_implementations×1 |
| outline-file | 8.2 → 5.0 | 1 → 1 | 13,384 → 34,773 | 0.010 → 0.008 | 80 → 97 | get_file_outline×1 |
| read-method | 5.6 → 3.3 | 1 → 1 | 13,635 → 34,367 | 0.008 → 0.005 | 85 → 95 | get_symbol_source×1 |
| find-callers | 7.8 → 3.5 | 4 → 1 | 21,042 → 34,471 | 0.015 → 0.006 | 25 → 100 | find_callers×1 |
| type-hierarchy | 10.4 → 3.5 | 2 → 1 | 20,553 → 34,406 | 0.015 → 0.006 | 72 → 97 | get_type_hierarchy×1 |
| project-graph | 6.3 → 3.4 | 2 → 1 | 16,976 → 35,174 | 0.025 → 0.006 | 40 → 100 | get_project_graph×1 |
| di-registrations | 10.2 → 4.5 | 1 → 1 | 16,774 → 35,345 | 0.030 → 0.009 | 45 → 92 | get_di_registrations×1 |
| endpoint-map | 14.4 → 15.0 | 5 → 4 | 46,567 → 80,455 | 0.054 → 0.057 | 90 → 92 | get_endpoint_map×1, get_workspace_status×1 |
| stack-trace | 11.2 → 7.1 | 3 → 1 | 30,089 → 35,533 | 0.038 → 0.013 | 62 → 92 | resolve_stack_trace×1 |

## eshop

| Task | Wall s (without → with) | Tool calls | Input tokens | Cost USD | Accuracy | Navigator tools used |
|---|---:|---:|---:|---:|---:|---|
| locate-type | 4.4 → 2.3 | 1 → 1 | 12,796 → 34,318 | 0.003 → 0.005 | 97 → 100 | find_symbol×1 |
| interface-usages | 4.3 → 4.4 | 1 → 1 | 13,155 → 34,834 | 0.007 → 0.010 | 92 → 90 | find_references×1 |
| implementations | 4.0 → 2.7 | 1 → 1 | 12,871 → 34,296 | 0.004 → 0.005 | 97 → 100 | find_implementations×1 |
| outline-file | 6.6 → 6.7 | 1 → 1 | 14,323 → 36,287 | 0.016 → 0.013 | 55 → 97 | get_file_outline×1 |
| read-method | 5.9 → 3.8 | 2 → 1 | 20,184 → 34,400 | 0.010 → 0.006 | 90 → 100 | get_symbol_source×1 |
| find-callers | 6.7 → 2.9 | 2 → 1 | 20,473 → 34,319 | 0.012 → 0.006 | 55 → 95 | find_callers×1 |
| type-hierarchy | 7.4 → 3.7 | 3 → 1 | 21,447 → 34,446 | 0.010 → 0.008 | 80 → 97 | get_type_hierarchy×1 |
| project-graph | 13.1 → 7.1 | 3 → 1 | 35,625 → 36,515 | 0.034 → 0.013 | 90 → 97 | get_project_graph×1 |
| di-registrations | 12.4 → 8.9 | 1 → 1 | 17,508 → 40,373 | 0.039 → 0.017 | 72 → 95 | get_di_registrations×1 |
| endpoint-map | 9.7 → 9.3 | 1 → 1 | 15,464 → 37,568 | 0.025 → 0.017 | 62 → 93 | get_endpoint_map×1 |
| stack-trace | 8.5 → 4.4 | 3 → 1 | 21,933 → 35,118 | 0.018 → 0.012 | 72 → 90 | resolve_stack_trace×1 |

## jellyfin

| Task | Wall s (without → with) | Tool calls | Input tokens | Cost USD | Accuracy | Navigator tools used |
|---|---:|---:|---:|---:|---:|---|
| locate-type | 6.1 → 2.4 | 1 → 1 | 12,968 → 34,455 | 0.003 → 0.005 | 95 → 98 | find_symbol×1 |
| interface-usages | 19.2 → 17.6 | 2 → 1 | 32,073 → 47,660 | 0.078 → 0.088 | 78 → 97 | find_references×1 |
| implementations | 6.8 → 4.2 | 2 → 1 | 23,876 → 34,440 | 0.018 → 0.006 | 68 → 92 | find_implementations×1 |
| outline-file | 26.5 → 20.7 | 2 → 1 | 35,697 → 46,324 | 0.078 → 0.042 | 72 → 95 | get_file_outline×1 |
| read-method | 3.9 → 2.7 | 1 → 1 | 16,216 → 34,453 | 0.018 → 0.006 | 96 → 98 | get_symbol_source×1 |
| find-callers | 22.6 → 11.9 | 3 → 1 | 63,929 → 36,288 | 0.166 → 0.026 | 8 → 100 | find_callers×1 |
| type-hierarchy | 16.0 → 6.4 | 5 → 1 | 41,191 → 34,685 | 0.074 → 0.011 | 72 → 90 | get_type_hierarchy×1 |
| project-graph | 13.2 → 12.2 | 2 → 1 | 35,390 → 38,994 | 0.068 → 0.025 | 80 → 97 | get_project_graph×1 |
| di-registrations | 16.1 → 10.8 | 5 → 1 | 32,061 → 51,355 | 0.036 → 0.020 | 45 → 88 | get_di_registrations×1 |
| endpoint-map | 19.6 → 9.1 | 3 → 3 | 36,242 → 54,269 | 0.050 → 0.026 | 45 → 62 | get_endpoint_map×1, get_workspace_status×1 |
| stack-trace | 10.7 → 7.6 | 2 → 1 | 26,407 → 35,789 | 0.027 → 0.012 | 72 → 85 | resolve_stack_trace×1 |

## orchardcore

| Task | Wall s (without → with) | Tool calls | Input tokens | Cost USD | Accuracy | Navigator tools used |
|---|---:|---:|---:|---:|---:|---|
| locate-type | 17.8 → 4.4 | 1 → 1 | 12,822 → 34,351 | 0.010 → 0.006 | 93 → 97 | find_symbol×1 |
| interface-usages | 15.4 → 17.6 | 2 → 1 | 22,869 → 55,148 | 0.031 → 0.108 | 8 → 85 | find_references×1 |
| implementations | 9.2 → 14.5 | 1 → 2 | 13,046 → 63,041 | 0.015 → 0.056 | 8 → 95 | find_implementations×2 |
| outline-file | 10.9 → 6.6 | 2 → 1 | 20,715 → 34,888 | 0.023 → 0.011 | 80 → 95 | get_file_outline×1 |
| read-method | 9.5 → 4.7 | 2 → 1 | 19,902 → 34,340 | 0.015 → 0.006 | 97 → 98 | get_symbol_source×1 |
| find-callers | 16.9 → 15.8 | 6 → 2 | 38,904 → 62,869 | 0.052 → 0.044 | 15 → 85 | find_callers×1 |
| type-hierarchy | 18.7 → 10.1 | 6 → 1 | 62,845 → 36,038 | 0.073 → 0.021 | 40 → 88 | get_type_hierarchy×1 |
| project-graph | 21.7 → 12.9 | 4 → 2 | 59,032 → 55,027 | 0.107 → 0.031 | 35 → 40 | get_project_graph×2 |
| di-registrations | 14.2 → 8.4 | 4 → 1 | 22,761 → 43,866 | 0.034 → 0.052 | 22 → 80 | get_di_registrations×1 |
| endpoint-map | 15.4 → 33.8 | 2 → 7 | 17,467 → 161,551 | 0.049 → 0.095 | 72 → 74 | get_endpoint_map×2 |
| stack-trace | 13.3 → 6.2 | 2 → 1 | 23,551 → 35,712 | 0.031 → 0.016 | 40 → 95 | resolve_stack_trace×1 |

## abp

| Task | Wall s (without → with) | Tool calls | Input tokens | Cost USD | Accuracy | Navigator tools used |
|---|---:|---:|---:|---:|---:|---|
| locate-type | 15.4 → 2.8 | 1 → 1 | 12,808 → 34,340 | 0.009 → 0.006 | 85 → 100 | find_symbol×1 |
| interface-usages | 16.5 → 16.7 | 3 → 3 | 33,415 → 75,493 | 0.039 → 0.036 | 35 → 30 | find_references×1 |
| implementations | 15.9 → 6.7 | 3 → 1 | 26,431 → 35,276 | 0.045 → 0.016 | 12 → 95 | find_implementations×1 |
| outline-file | 18.1 → 9.8 | 3 → 1 | 30,529 → 39,916 | 0.046 → 0.042 | 88 → 97 | get_file_outline×1 |
| read-method | 5.8 → 6.2 | 1 → 1 | 13,499 → 34,445 | 0.014 → 0.008 | 85 → 98 | get_symbol_source×1 |
| find-callers | 14.6 → 7.2 | 5 → 1 | 34,231 → 34,698 | 0.043 → 0.010 | 30 → 100 | find_callers×1 |
| type-hierarchy | 13.9 → 9.7 | 4 → 3 | 24,456 → 78,917 | 0.037 → 0.040 | 62 → 66 | get_type_hierarchy×1, get_workspace_status×1 |
| project-graph | 16.7 → 8.7 | 3 → 1 | 28,708 → 36,891 | 0.058 → 0.026 | 20 → 80 | get_project_graph×1 |
| di-registrations | 13.1 → 13.1 | 2 → 1 | 26,263 → 58,160 | 0.046 → 0.117 | 40 → 82 | get_di_registrations×1 |
| endpoint-map | 14.2 → 6.4 | 1 → 1 | 18,951 → 35,081 | 0.051 → 0.013 | 30 → 92 | get_endpoint_map×1 |
| stack-trace | 10.8 → 7.2 | 1 → 1 | 14,418 → 35,685 | 0.024 → 0.017 | 82 → 92 | resolve_stack_trace×1 |
