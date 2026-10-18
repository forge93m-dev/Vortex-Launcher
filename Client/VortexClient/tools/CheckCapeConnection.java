import java.net.URI;
import java.net.http.*;
import java.time.Duration;

/** No game credentials, UUIDs or private files are read by this diagnostic. */
class CheckCapeConnection {
    public static void main(String[] args) {
        try(var client=HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(4)).followRedirects(HttpClient.Redirect.NEVER).build()) {
            var request=HttpRequest.newBuilder(URI.create("https://vortex-cosmetics.forge93-m.workers.dev/v1/lookup"))
                .timeout(Duration.ofSeconds(5)).header("Content-Type","application/json")
                .POST(HttpRequest.BodyPublishers.ofString("{\"uuids\":[],\"custom\":true}")).build();
            var response=client.send(request,HttpResponse.BodyHandlers.ofByteArray());
            System.out.println("Cape lookup status="+response.statusCode()+"; protocol="+response.version()+"; bytes="+response.body().length);
        } catch(Exception error) {
            for(Throwable cause=error;cause!=null;cause=cause.getCause()) System.out.println(cause.getClass().getName()+": "+cause.getMessage());
        }
    }
}
