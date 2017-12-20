package security

import (
	"log"
	"net/http"

	"github.com/casbin/casbin"

	"github.com/urfave/negroni"
)

// AuthorizationMiddleware enforces authorization policies of incoming requests
func AuthorizationMiddleware(policy, model interface{}) negroni.HandlerFunc {
	enforcer := casbin.NewSyncedEnforcer(policy, model)
	return negroni.HandlerFunc(func(rw http.ResponseWriter, r *http.Request, next http.HandlerFunc) {
		userInfo := r.Context().Value(userInfoKey).(UserInfo)

		// this is work in progress
		subject := userInfo.Email()
		object := r.RequestURI
		action := r.Method

		result, err := enforcer.EnforceSafe(subject, object, action)
		if err != nil {
			log.Println("Failed to validate request", err)
		}

		if result {
			next(rw, r)
		} else {
			http.Error(rw, "Access denied", http.StatusUnauthorized)
		}
	})
}