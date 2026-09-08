## Summary

Describe the user-visible outcome and affected boundary.

## Validation

- [ ] `dotnet build EasyExile.slnx -c Release` passes
- [ ] All tests pass
- [ ] NuGet vulnerability audit is clean
- [ ] New behavior has deterministic tests
- [ ] Optional or unavailable snapshot data is handled safely

## Safety

- [ ] Process access remains read-only
- [ ] Radar code does not access memory readers or offsets
- [ ] Build mismatch still fails closed
- [ ] No logs, dumps, credentials, account data, or generated cache files are included
