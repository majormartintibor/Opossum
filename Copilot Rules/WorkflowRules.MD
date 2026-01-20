Always create tests for any code you write.
Tests with pure functions belong to the appropriate Unit Test project.
Tests with side effects (e.g., database access, file system access, network access) belong to the appropriate Integration Test project.
Ensure tests are isolated and can run independently of each other.
Create Benchmark tests for performance-critical code sections in the Opossum.BenchmarkTests project.
You must always make sure the code compiles. Fix any compilation errors before committing your code.
Before committing code, run all tests to ensure they pass successfully.