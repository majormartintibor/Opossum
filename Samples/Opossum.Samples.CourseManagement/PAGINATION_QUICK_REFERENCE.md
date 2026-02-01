# Pagination and Filtering - Quick Reference

## 🚀 Quick Start

### Students Endpoint

```http
GET /students?pageNumber=1&pageSize=50&tierFilter=Professional&isMaxedOut=false&sortBy=enrollmentCount
```

### Courses Endpoint

```http
GET /courses?pageNumber=1&pageSize=50&isFull=false&sortBy=enrollmentCount
```

---

## 📋 API Reference

### GET /students

**Query Parameters:**

| Parameter | Type | Default | Options |
|-----------|------|---------|---------|
| `pageNumber` | int | 1 | Any positive integer |
| `pageSize` | int | 50 | 1-100 (capped at 100) |
| `tierFilter` | enum? | null | `Basic`, `Standard`, `Professional`, `Master` |
| `isMaxedOut` | bool? | null | `true`, `false` |
| `sortBy` | string? | `name` | `tier`, `enrollmentCount`, `name` |

**Response:**
```json
{
  "items": [
    {
      "studentId": "guid",
      "firstName": "string",
      "lastName": "string",
      "email": "string",
      "enrollmentTier": "Professional",
      "currentEnrollmentCount": 4,
      "maxEnrollmentCount": 6,
      "isMaxedOut": false
    }
  ],
  "totalCount": 10000,
  "pageNumber": 1,
  "pageSize": 50,
  "totalPages": 200,
  "hasPreviousPage": false,
  "hasNextPage": true
}
```

### GET /courses

**Query Parameters:**

| Parameter | Type | Default | Options |
|-----------|------|---------|---------|
| `pageNumber` | int | 1 | Any positive integer |
| `pageSize` | int | 50 | 1-100 (capped at 100) |
| `isFull` | bool? | null | `true`, `false` |
| `sortBy` | string? | `name` | `name`, `enrollmentCount`, `capacity` |

**Response:**
```json
{
  "items": [
    {
      "courseId": "guid",
      "name": "string",
      "maxStudentCount": 30,
      "currentEnrollmentCount": 25,
      "isFull": false
    }
  ],
  "totalCount": 2200,
  "pageNumber": 1,
  "pageSize": 50,
  "totalPages": 44,
  "hasPreviousPage": false,
  "hasNextPage": true
}
```

---

## 💡 Common Use Cases

### Find Available Courses
```bash
curl "http://localhost:5000/courses?isFull=false"
```

### Find Students Who Can Enroll More
```bash
curl "http://localhost:5000/students?isMaxedOut=false&sortBy=enrollmentCount"
```

### Find Professional Tier Students with Capacity
```bash
curl "http://localhost:5000/students?tierFilter=Professional&isMaxedOut=false"
```

### Get Most Popular Courses
```bash
curl "http://localhost:5000/courses?sortBy=enrollmentCount&pageSize=20"
```

### Find Basic Tier Students at Limit
```bash
curl "http://localhost:5000/students?tierFilter=Basic&isMaxedOut=true"
```

---

## ⚡ Performance

With **10,000 students, 2,200 courses, 50,000 enrollments:**

- Students queries: **~300-500ms**
- Courses queries: **~200-400ms**

Optimized for:
- Up to 100,000 events
- Up to 20,000 aggregates
- Sub-second query latency

---

## 📁 Files Modified

1. `Shared/PaginatedResponse.cs` - ✨ NEW
2. `StudentShortInfo/GetStudentsShortInfo.cs` - ♻️ Enhanced
3. `CourseShortInfo/GetCoursesShortInfo.cs` - ♻️ Enhanced

**Opossum Library:** ✅ No changes needed

---

## 🎯 Features Implemented

- ✅ Pagination (default: 50, max: 100)
- ✅ Students: Filter by tier
- ✅ Students: Filter by maxed-out status
- ✅ Students: Sort by tier/enrollment/name
- ✅ Courses: Filter by full status
- ✅ Courses: Sort by enrollment/capacity/name
- ✅ Enrollment counts always visible
- ✅ Computed properties (IsMaxedOut, IsFull)
- ✅ Response metadata (total pages, has next/previous)

---

For detailed documentation, see [PAGINATION_AND_FILTERING.md](./PAGINATION_AND_FILTERING.md)
