# Dixon Command Center UI update

The contributor Daily Report uses a category-first entry matrix. Each production line and model has side-by-side KPI value columns (such as Plan and Actual), so contributors can enter paired values together.

### Added / improved
- Real Dixon logo from `src/assets/dixon-logo.jpeg` in shared headers and contributor navigation.
- Larger, more readable Inter / Manrope typography.
- Shared footer across admin, dashboard and approval pages; contributor already has its own footer.
- Admin workspace with Users, Plants, Customers, Categories, Models, Production Lines, line/model mapping and KPI Plans.
- User create/edit/deactivate and brand-access assignment.
- Viewer dashboard plant selector for switching read-only progress data across all active plants.
- Catalog create/edit/deactivate.
- Monthly KPI plan storage through the backend and `KPI_Master`.
- Approval detail shows submitted daily values plus the matching monthly KPI plan reference.
- Approval overview and history pages.
- Contributor submissions and workspace guide pages.
- Daily Report groups rows by KPI category and places relevant values such as Plan, Actual, Target and Installed side by side under each production line/model.
- Daily Report remains horizontally scrollable and preserves line/model selection, KPI plan prefills, draft saves and submissions.
